using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Floor.Queries;

public sealed record GetTableMapQuery(bool IncludeInactive) : IQuery<TableMapDto>, IRequirePermission
{
    public string Permission => Permissions.TablesView;
}

internal sealed class GetTableMapHandler : IQueryHandler<GetTableMapQuery, TableMapDto>
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;

    public GetTableMapHandler(IAppDbContext db, IClock clock, ICurrentUser currentUser)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
    }

    public async Task<Result<TableMapDto>> Handle(GetTableMapQuery request, CancellationToken cancellationToken)
    {
        var includeInactive = request.IncludeInactive;
        if (includeInactive && !(_currentUser.IsInRole(Roles.Admin) || _currentUser.IsInRole(Roles.Manager)))
        {
            return AppErrors.Forbidden("Only managers can list inactive tables.");
        }

        // Taken before reading: any event stamped later is not (or not certainly) reflected in this map.
        var serverTime = _clock.UtcNow;

        var sections = await _db.Sections.AsNoTracking()
            .Where(s => includeInactive || s.IsActive)
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Name)
            .ToListAsync(cancellationToken);
        var tables = await _db.Tables.AsNoTracking()
            .Include(t => t.CurrentOrder).Include(t => t.MergedInto).Include(t => t.MergedTables)
            .Where(t => includeInactive || t.IsActive)
            .ToListAsync(cancellationToken);

        // Running total of each table's open order, for the tile.
        var orderIds = tables.Where(t => t.CurrentOrderId != null).Select(t => t.CurrentOrderId!.Value).ToList();
        var totals = orderIds.Count == 0
            ? new Dictionary<int, decimal>()
            : await _db.OrderItems.AsNoTracking()
                .Where(i => orderIds.Contains(i.OrderId) && i.Status != HotelPOS.Contracts.Enums.OrderItemStatus.Cancelled)
                .GroupBy(i => i.OrderId)
                .Select(g => new { OrderId = g.Key, Total = g.Sum(i => (i.UnitPrice + i.Modifiers.Sum(m => m.PriceDelta)) * i.Quantity) })
                .ToDictionaryAsync(x => x.OrderId, x => x.Total, cancellationToken);

        var windowEnd = serverTime.AddMinutes(HotelPOS.Contracts.Reservations.ReservationLimits.ReservedSoonWindowMinutes);
        var upcomingReservations = await _db.Reservations.AsNoTracking()
            .Where(r => r.Status == HotelPOS.Contracts.Enums.ReservationStatus.Booked
                && r.StartTimeUtc <= windowEnd
                && r.EndTimeUtc >= serverTime)
            .OrderBy(r => r.StartTimeUtc)
            .ToListAsync(cancellationToken);

        var upcomingByTable = upcomingReservations
            .GroupBy(r => r.TableId)
            .ToDictionary(
                g => g.Key,
                g => HotelPOS.Application.Features.Reservations.ReservationMapping.ToSummaryDto(g.First()));

        var bySection = tables.ToLookup(t => t.SectionId);
        return new TableMapDto
        {
            ServerTimeUtc = serverTime,
            Sections = sections.Select(s => new TableMapSectionDto
            {
                Id = s.Id,
                Name = s.Name,
                SortOrder = s.SortOrder,
                IsActive = s.IsActive,
                Tables = bySection[s.Id]
                    .OrderBy(t => t.Code, StringComparer.Ordinal)
                    .Select(t => TableMapping.ToDto(t, s.Name, upcomingByTable.GetValueOrDefault(t.Id),
                        t.CurrentOrderId is { } orderId && totals.TryGetValue(orderId, out var total) ? total : null))
                    .ToList(),
            }).ToList(),
        };
    }
}
