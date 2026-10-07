using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Common.Security;
using HotelPOS.Application.Devices;
using HotelPOS.Contracts.Auth;
using HotelPOS.Contracts.Enums;
using HotelPOS.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HotelPOS.Application.Auth;

public sealed class AuthService : IAuthService
{
    // Verified when the username does not exist, so response time does not reveal valid usernames.
    private static string? s_dummyHash;

    private readonly IAppDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenService _tokens;
    private readonly IDeviceService _devices;
    private readonly IAuditService _audit;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly JwtOptions _jwt;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IAppDbContext db,
        IPasswordHasher hasher,
        ITokenService tokens,
        IDeviceService devices,
        IAuditService audit,
        ICurrentUser currentUser,
        IClock clock,
        IOptions<JwtOptions> jwt,
        ILogger<AuthService> logger)
    {
        _db = db;
        _hasher = hasher;
        _tokens = tokens;
        _devices = devices;
        _audit = audit;
        _currentUser = currentUser;
        _clock = clock;
        _jwt = jwt.Value;
        _logger = logger;
    }

    public async Task<Result<LoginResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var normalized = User.Normalize(request.Username);
        var user = await UsersWithRoles().FirstOrDefaultAsync(u => u.NormalizedUsername == normalized, cancellationToken);

        if (user is null)
        {
            _hasher.Verify(s_dummyHash ??= _hasher.Hash(Guid.NewGuid().ToString("N")), request.Password);
            _audit.Record(AuditActions.LoginFailed, nameof(User), null,
                newValues: new { request.Username, Reason = "UnknownUser", request.DeviceName },
                actor: new AuditActor(null, request.Username));
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogWarning("Login failed for unknown user {Username}", request.Username);
            return AppErrors.InvalidCredentials();
        }

        if (user.IsLockedOut(now))
        {
            return AppErrors.AccountLocked();
        }

        var check = _hasher.Verify(user.PasswordHash, request.Password);
        if (check == PasswordCheck.Failed)
        {
            user.RecordFailedLogin();
            _audit.Record(AuditActions.LoginFailed, nameof(User), user.Id.ToString(),
                newValues: new { Reason = "WrongPassword", user.FailedLoginCount, request.DeviceName },
                actor: new AuditActor(user.Id, user.Username));
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogWarning("Login failed for user {Username}: wrong password", user.Username);
            return AppErrors.InvalidCredentials();
        }

        if (!user.IsActive)
        {
            return AppErrors.AccountDisabled();
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
        var response = IssueTokens(user, device, now);
        _audit.Record(AuditActions.LoginSucceeded, nameof(User), user.Id.ToString(),
            newValues: new { DeviceName = device?.Name },
            actor: new AuditActor(user.Id, user.Username));

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("User {Username} logged in on device {DeviceName}", user.Username, device?.Name ?? "(none)");
        return response;
    }

    public async Task<Result<LoginResponse>> RefreshAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var hash = _tokens.HashRefreshToken(request.RefreshToken);
        var stored = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (stored is null)
        {
            return AppErrors.Unauthenticated();
        }

        if (stored.IsRevoked)
        {
            if (stored.ReplacedByTokenHash is not null)
            {
                // A rotated token was presented again: it may have been stolen. Revoke every session of this user.
                await RevokeAllAsync(stored.UserId, now, "Reuse detected", cancellationToken);
                _audit.Record(AuditActions.RefreshTokenReuse, nameof(User), stored.UserId.ToString(),
                    actor: new AuditActor(stored.UserId, null));
                await _db.SaveChangesAsync(cancellationToken);
                _logger.LogWarning("Refresh token reuse detected for user {UserId}; all sessions revoked", stored.UserId);
            }

            return AppErrors.Unauthenticated();
        }

        if (!stored.IsActive(now))
        {
            return AppErrors.Unauthenticated();
        }

        var user = await UsersWithRoles().FirstAsync(u => u.Id == stored.UserId, cancellationToken);
        if (!user.IsActive)
        {
            stored.Revoke(now, "User disabled");
            await _db.SaveChangesAsync(cancellationToken);
            return AppErrors.AccountDisabled();
        }

        Device? device = null;
        if (stored.DeviceId is { } deviceId)
        {
            device = await _db.Devices.FirstOrDefaultAsync(d => d.Id == deviceId, cancellationToken);
            if (device is { IsActive: false })
            {
                stored.Revoke(now, "Device disabled");
                await _db.SaveChangesAsync(cancellationToken);
                return AppErrors.DeviceDisabled(device.Name);
            }

            device?.Touch(now);
        }

        var response = IssueTokens(user, device, now);
        stored.Revoke(now, "Rotated", _tokens.HashRefreshToken(response.RefreshToken));
        await _db.SaveChangesAsync(cancellationToken);
        return response;
    }

    public async Task<Result> LogoutAsync(LogoutRequest request, CancellationToken cancellationToken = default)
    {
        var hash = _tokens.HashRefreshToken(request.RefreshToken);
        var stored = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (stored is not null && !stored.IsRevoked)
        {
            stored.Revoke(_clock.UtcNow, "Logout");
            await _db.SaveChangesAsync(cancellationToken);
        }

        // Logging out is idempotent: an unknown or already revoked token is not an error.
        return Result.Success();
    }

    public async Task<Result<CurrentUserDto>> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return AppErrors.Unauthenticated();
        }

        var user = await UsersWithRoles().AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        return user is null ? AppErrors.Unauthenticated() : ToCurrentUser(user);
    }

    public async Task<Result> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return AppErrors.Unauthenticated();
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            return AppErrors.Unauthenticated();
        }

        if (_hasher.Verify(user.PasswordHash, request.CurrentPassword) == PasswordCheck.Failed)
        {
            return AppErrors.Validation(nameof(request.CurrentPassword).ToCamelCase(), "The current password is not correct.");
        }

        user.SetPasswordHash(_hasher.Hash(request.NewPassword), mustChangePassword: false, changedAt: _clock.UtcNow);
        _audit.Record(AuditActions.PasswordChanged, nameof(User), user.Id.ToString());
        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    internal static CurrentUserDto ToCurrentUser(User user)
    {
        var roles = user.RoleNames.OrderBy(r => r, StringComparer.Ordinal).ToList();
        return new CurrentUserDto
        {
            Id = user.Id,
            Username = user.Username,
            DisplayName = user.DisplayName,
            Roles = roles,
            Permissions = RolePermissionMap.For(roles),
            MustChangePassword = user.MustChangePassword,
        };
    }

    private IQueryable<User> UsersWithRoles() =>
        _db.Users.Include(u => u.UserRoles).ThenInclude(ur => ur.Role);

    private LoginResponse IssueTokens(User user, Device? device, DateTime now)
    {
        var current = ToCurrentUser(user);
        var access = _tokens.CreateAccessToken(user, current.Roles, current.Permissions, device?.Id, device?.Name);
        var refresh = _tokens.GenerateRefreshToken();
        var refreshExpires = now.AddHours(_jwt.RefreshTokenHours);

        _db.RefreshTokens.Add(new RefreshToken(user.Id, _tokens.HashRefreshToken(refresh), device?.Id, refreshExpires, _currentUser.IpAddress));

        return new LoginResponse
        {
            AccessToken = access.Token,
            AccessTokenExpiresAtUtc = access.ExpiresAtUtc,
            RefreshToken = refresh,
            RefreshTokenExpiresAtUtc = refreshExpires,
            User = current,
            DeviceId = device?.Id,
        };
    }

    private async Task RevokeAllAsync(int userId, DateTime now, string reason, CancellationToken cancellationToken)
    {
        var active = await _db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var token in active)
        {
            token.Revoke(now, reason);
        }
    }
}
