using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Users;
using HotelPOS.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HotelPOS.Application.Security;

public sealed class SessionService : ISessionService
{
    private readonly IAppDbContext _db;
    private readonly IAuditService _audit;
    private readonly IClock _clock;
    private readonly ILogger<SessionService> _logger;

    public SessionService(
        IAppDbContext db,
        IAuditService audit,
        IClock clock,
        ILogger<SessionService> logger)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<UserSessionDto>>> GetUserSessionsAsync(int userId, CancellationToken cancellationToken = default)
    {
        var userExists = await _db.Users.AnyAsync(u => u.Id == userId, cancellationToken);
        if (!userExists)
        {
            return AppErrors.NotFound("User", userId);
        }

        // Each refresh rotates the token, so revoked rows are mostly rotations rather than sessions: a session in use is
        // an unrevoked, unexpired token.
        var now = _clock.UtcNow;
        var tokens = await _db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > now)
            .OrderByDescending(t => t.CreatedAt)
            .Take(50)
            .ToListAsync(cancellationToken);

        var deviceIds = tokens.Where(t => t.DeviceId.HasValue).Select(t => t.DeviceId!.Value).Distinct().ToList();
        var devices = await _db.Devices
            .Where(d => deviceIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => d.Name, cancellationToken);

        var dtos = tokens.Select(t => new UserSessionDto
        {
            Id = t.Id,
            DeviceId = t.DeviceId,
            DeviceName = t.DeviceId.HasValue && devices.TryGetValue(t.DeviceId.Value, out var name) ? name : null,
            IpAddress = t.CreatedByIp,
            CreatedAtUtc = t.CreatedAt,
            ExpiresAtUtc = t.ExpiresAt,
            IsActive = t.IsActive(now),
            IsRevoked = t.IsRevoked,
            RevokedReason = t.RevokedReason,
        }).ToList();

        return dtos;
    }

    public async Task<Result> LogoutAllAsync(int userId, CancellationToken cancellationToken = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return AppErrors.NotFound("User", userId);
        }

        var now = _clock.UtcNow;
        var activeTokens = await _db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var token in activeTokens)
        {
            token.Revoke(now, "Logged out everywhere");
        }

        _audit.Record(AuditActions.UserLoggedOutAll, nameof(User), user.Id.ToString(),
            newValues: new { RevokedSessionsCount = activeTokens.Count });

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Revoked all ({Count}) active sessions for user {Username}", activeTokens.Count, user.Username);
        return Result.Success();
    }

    public async Task<Result> UnlockUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return AppErrors.NotFound("User", userId);
        }

        var wasLocked = user.IsLockedOut(_clock.UtcNow) || user.FailedLoginCount > 0;
        user.Unlock();

        if (wasLocked)
        {
            _audit.Record(AuditActions.UserUnlocked, nameof(User), user.Id.ToString());
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("User {Username} was unlocked by administrator", user.Username);
        }

        return Result.Success();
    }
}

