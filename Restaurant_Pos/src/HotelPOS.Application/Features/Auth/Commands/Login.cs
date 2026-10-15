using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Features.Devices;
using HotelPOS.Application.Features.Licensing;
using HotelPOS.Application.Features.Security;
using HotelPOS.Contracts.Auth;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Security;
using HotelPOS.Contracts.Devices;
using HotelPOS.Contracts.Enums;
using HotelPOS.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HotelPOS.Application.Features.Auth.Commands;

// Anonymous. A failed attempt is kept although the result is a failure: it is audited and counts towards the lockout.
public sealed record LoginCommand(LoginRequest Input)
    : ICommand<LoginResponse>, IHasInput<LoginRequest>, IAuditedCommand, ICommitOnFailure;

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Username).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(PasswordPolicy.MaxLength);
        RuleFor(x => x.DeviceName!)
            .Length(DeviceNameRules.MinLength, DeviceNameRules.MaxLength)
            .Matches(DeviceNameRules.Pattern).WithMessage("Device name may contain letters, digits, '-' and '_' only.")
            .When(x => !string.IsNullOrWhiteSpace(x.DeviceName));
        RuleFor(x => x.DeviceType).IsInEnum().When(x => x.DeviceType is not null);
        RuleFor(x => x.MachineName).MaximumLength(100);
        RuleFor(x => x.AppVersion).MaximumLength(30);
    }
}

internal sealed class LoginHandler : ICommandHandler<LoginCommand, LoginResponse>
{
    // Verified when the username does not exist, so response time does not reveal valid usernames.
    private static string? s_dummyHash;

    private readonly IAppDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IAuditTrail _audit;
    private readonly DeviceRegistry _devices;
    private readonly RolePermissions _permissions;
    private readonly AuthTokens _tokens;
    private readonly IClock _clock;
    private readonly ILicenseService _licenses;
    private readonly ILogger<LoginHandler> _logger;

    public LoginHandler(
        IAppDbContext db,
        IPasswordHasher hasher,
        IAuditTrail audit,
        DeviceRegistry devices,
        RolePermissions permissions,
        AuthTokens tokens,
        IClock clock,
        ILicenseService licenses,
        ILogger<LoginHandler> logger)
    {
        _db = db;
        _hasher = hasher;
        _audit = audit;
        _devices = devices;
        _permissions = permissions;
        _tokens = tokens;
        _clock = clock;
        _licenses = licenses;
        _logger = logger;
    }

    public async Task<Result<LoginResponse>> Handle(LoginCommand command, CancellationToken cancellationToken)
    {
        var request = command.Input;
        var now = _clock.UtcNow;
        var normalized = User.Normalize(request.Username);
        var user = await _tokens.UsersWithRoles().FirstOrDefaultAsync(u => u.NormalizedUsername == normalized, cancellationToken);

        if (user is null)
        {
            _hasher.Verify(s_dummyHash ??= _hasher.Hash(Guid.NewGuid().ToString("N")), request.Password);
            _audit.Add(AuditActions.LoginFailed, nameof(User), null,
                newValues: new { request.Username, Reason = "UnknownUser", request.DeviceName },
                actor: new AuditActor(null, request.Username));
            _logger.LogWarning("Login failed for unknown user {Username}", request.Username);
            return AppErrors.InvalidCredentials();
        }

        if (user.IsLockedOut(now))
        {
            _audit.Add(AuditActions.LoginFailed, nameof(User), user.Id.ToString(),
                newValues: new { Reason = "Locked", user.LockedUntil, request.DeviceName },
                actor: new AuditActor(user.Id, user.Username));
            return AppErrors.AccountLocked();
        }

        var check = _hasher.Verify(user.PasswordHash, request.Password);
        if (check == PasswordCheck.Failed)
        {
            var locked = await LoginFailures.RecordAsync(_db, _audit, user, now, "WrongPassword", request.DeviceName, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            if (locked)
            {
                _logger.LogWarning("User {Username} locked out until {LockedUntil} after {FailedCount} failed attempts",
                    user.Username, user.LockedUntil, user.FailedLoginCount);
                return AppErrors.AccountLocked();
            }

            _logger.LogWarning("Login failed for user {Username}: wrong password", user.Username);
            return AppErrors.InvalidCredentials();
        }

        if (!user.IsActive)
        {
            return AppErrors.AccountDisabled();
        }

        var permissions = await _permissions.ForRolesAsync(user.RoleNames, cancellationToken);

        // Licence: when expired only an administrator may sign in (to see the data and apply a key); a terminal over
        // the licensed count is refused before it is registered.
        var licence = await _licenses.EvaluateAsync(cancellationToken);
        if (licence.IsExpired && !permissions.Contains(Permissions.LicenseManage))
        {
            return new AppError(ErrorCodes.LicenseExpired, licence.Problem ?? "The licence has expired. Ask an administrator to apply a new licence key.");
        }

        if (!string.IsNullOrWhiteSpace(request.DeviceName) && await _licenses.CheckTerminalAsync(request.DeviceName, cancellationToken) is { } limit)
        {
            return limit;
        }

        Device? device = null;
        if (!string.IsNullOrWhiteSpace(request.DeviceName))
        {
            device = await _devices.UpsertAsync(
                request.DeviceName,
                request.DeviceType ?? DeviceType.Waiter,
                request.MachineName,
                request.AppVersion,
                stationId: null,
                cancellationToken);

            if (!device.IsActive)
            {
                return AppErrors.DeviceDisabled(device.Name);
            }

            device.Touch(now);
        }

        if (check == PasswordCheck.SuccessRehashNeeded)
        {
            user.UpgradePasswordHash(_hasher.Hash(request.Password));
        }

        user.RecordSuccessfulLogin(now);
        var response = _tokens.Issue(user, device, permissions, now);
        _audit.Add(AuditActions.LoginSucceeded, nameof(User), user.Id.ToString(),
            newValues: new { DeviceName = device?.Name },
            actor: new AuditActor(user.Id, user.Username));

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("User {Username} logged in on device {DeviceName}", user.Username, device?.Name ?? "(none)");
        return response;
    }
}
