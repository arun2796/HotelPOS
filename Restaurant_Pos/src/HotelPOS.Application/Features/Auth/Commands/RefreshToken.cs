using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Features.Security;
using HotelPOS.Contracts.Auth;
using HotelPOS.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HotelPOS.Application.Features.Auth.Commands;

// Anonymous; rotates the refresh token. Some refusals change state that must be kept although the result is a
// failure: a replayed token revokes every session of the user, a disabled user or device revokes the token.
public sealed record RefreshTokenCommand(RefreshTokenRequest Input)
    : ICommand<LoginResponse>, IHasInput<RefreshTokenRequest>, IAuditedCommand, ICommitOnFailure;

public sealed class RefreshTokenRequestValidator : AbstractValidator<RefreshTokenRequest>
{
    public RefreshTokenRequestValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(200);
    }
}

internal sealed class RefreshTokenHandler : ICommandHandler<RefreshTokenCommand, LoginResponse>
{
    private readonly IAppDbContext _db;
    private readonly ITokenService _tokenService;
    private readonly IAuditTrail _audit;
    private readonly RolePermissions _permissions;
    private readonly AuthTokens _tokens;
    private readonly IClock _clock;
    private readonly ILogger<RefreshTokenHandler> _logger;

    public RefreshTokenHandler(
        IAppDbContext db,
        ITokenService tokenService,
        IAuditTrail audit,
        RolePermissions permissions,
        AuthTokens tokens,
        IClock clock,
        ILogger<RefreshTokenHandler> logger)
    {
        _db = db;
        _tokenService = tokenService;
        _audit = audit;
        _permissions = permissions;
        _tokens = tokens;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<LoginResponse>> Handle(RefreshTokenCommand command, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var hash = _tokenService.HashRefreshToken(command.Input.RefreshToken);
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
                var active = await _db.RefreshTokens
                    .Where(t => t.UserId == stored.UserId && t.RevokedAt == null)
                    .ToListAsync(cancellationToken);
                foreach (var token in active)
                {
                    token.Revoke(now, "Reuse detected");
                }

                _audit.Add(AuditActions.RefreshTokenReuse, nameof(User), stored.UserId.ToString(),
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

        var user = await _tokens.UsersWithRoles().FirstAsync(u => u.Id == stored.UserId, cancellationToken);
        if (!user.IsActive)
        {
            stored.Revoke(now, "User disabled");
            await _db.SaveChangesAsync(cancellationToken);
            return AppErrors.AccountDisabled();
        }

        if (user.IsLockedOut(now))
        {
            return AppErrors.AccountLocked();
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

        var permissions = await _permissions.ForRolesAsync(user.RoleNames, cancellationToken);
        var response = _tokens.Issue(user, device, permissions, now);
        stored.Revoke(now, "Rotated", _tokenService.HashRefreshToken(response.RefreshToken));
        await _db.SaveChangesAsync(cancellationToken);
        return response;
    }
}
