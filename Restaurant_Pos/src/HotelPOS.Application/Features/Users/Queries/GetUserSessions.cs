using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Security;
using HotelPOS.Contracts.Users;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Users.Queries;

public sealed record GetUserSessionsQuery(int UserId) : IQuery<IReadOnlyList<UserSessionDto>>, IRequirePermission
{
    public string Permission => Permissions.UsersManage;
}

internal sealed class GetUserSessionsHandler : IQueryHandler<GetUserSessionsQuery, IReadOnlyList<UserSessionDto>>
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;

    public GetUserSessionsHandler(IAppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<UserSessionDto>>> Handle(GetUserSessionsQuery request, CancellationToken cancellationToken)
    {
        var userId = request.UserId;
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

        return tokens.Select(t => new UserSessionDto
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
    }
}
