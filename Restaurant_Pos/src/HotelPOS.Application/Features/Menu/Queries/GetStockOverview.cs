using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Menu.Queries;

// Every item whose count is kept: what was prepared, what is promised to open orders, what was sold, refilled and
// wasted today, what is left, the status, and who touched it last. One source for every screen.
public sealed record GetStockOverviewQuery : IQuery<StockOverviewDto>, IRequirePermission
{
    public string Permission => Permissions.MenuView;
}

internal sealed class GetStockOverviewHandler : IQueryHandler<GetStockOverviewQuery, StockOverviewDto>
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;

    public GetStockOverviewHandler(IAppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result<StockOverviewDto>> Handle(GetStockOverviewQuery request, CancellationToken cancellationToken)
    {
        var items = await _db.MenuItems.AsNoTracking()
            .Where(i => i.IsActive && i.StockMode != StockTrackingMode.None && i.StockRemaining != null)
            .OrderBy(i => i.Category!.SortOrder).ThenBy(i => i.Name)
            .Select(i => new
            {
                i.Id,
                i.Name,
                Category = i.Category!.Name,
                Station = i.PreparationStation == null ? string.Empty : i.PreparationStation.Name,
                i.StockMode,
                i.StockOpening,
                i.StockRemaining,
                i.IsAvailable,
                i.ReorderLevel,
            })
            .ToListAsync(cancellationToken);
        var ids = items.Select(i => i.Id).ToList();

        var (from, to) = await _db.BusinessDayAsync(_clock, null, cancellationToken);
        var today = (await _db.StockMovements.AsNoTracking()
                .Where(m => ids.Contains(m.MenuItemId) && m.CreatedAt >= from && m.CreatedAt < to)
                .GroupBy(m => new { m.MenuItemId, m.Type })
                .Select(g => new { g.Key.MenuItemId, g.Key.Type, Total = g.Sum(m => m.QuantityChanged) })
                .ToListAsync(cancellationToken))
            .ToLookup(x => x.MenuItemId);

        // Promised to the guests but not served yet: dishes of orders that are still with the kitchen.
        var reserved = (await _db.Orders.AsNoTracking()
                .Where(o => o.Status == OrderStatus.Submitted || o.Status == OrderStatus.Accepted
                    || o.Status == OrderStatus.Preparing || o.Status == OrderStatus.Ready)
                .SelectMany(o => o.Items)
                .Where(i => ids.Contains(i.MenuItemId) && i.Status == OrderItemStatus.Sent)
                .GroupBy(i => i.MenuItemId)
                .Select(g => new { MenuItemId = g.Key, Quantity = g.Sum(i => i.Quantity) })
                .ToListAsync(cancellationToken))
            .ToDictionary(x => x.MenuItemId, x => x.Quantity);

        var last = (await _db.StockMovements.AsNoTracking()
                .Where(m => ids.Contains(m.MenuItemId))
                .GroupBy(m => m.MenuItemId)
                .Select(g => g.OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).Select(m => new { m.MenuItemId, m.CreatedAt, m.CreatedBy }).First())
                .ToListAsync(cancellationToken))
            .ToDictionary(x => x.MenuItemId);
        var userIds = last.Values.Where(l => l.CreatedBy != null).Select(l => l.CreatedBy!.Value).Distinct().ToList();
        var users = await _db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);

        int Sum(int id, params StockMovementType[] types) => today[id].Where(t => types.Contains(t.Type)).Sum(t => t.Total);

        var rows = items.Select(i =>
        {
            last.TryGetValue(i.Id, out var movement);
            return new StockOverviewRowDto
            {
                MenuItemId = i.Id,
                ItemName = i.Name,
                Category = i.Category,
                Station = i.Station,
                Mode = i.StockMode,
                Opening = i.StockOpening ?? 0,
                Reserved = reserved.GetValueOrDefault(i.Id),
                Sold = -Sum(i.Id, StockMovementType.OrderReservation, StockMovementType.OrderRelease),
                Refilled = Sum(i.Id, StockMovementType.Refill),
                Wasted = -Sum(i.Id, StockMovementType.Waste),
                Available = i.StockRemaining ?? 0,
                ReorderLevel = i.ReorderLevel,
                Status = StockStatusRules.For(i.StockMode, i.StockRemaining, i.IsAvailable, i.ReorderLevel),
                LastUpdatedUtc = movement?.CreatedAt,
                UpdatedBy = movement?.CreatedBy is { } uid && users.TryGetValue(uid, out var name) ? name : null,
            };
        }).ToList();

        return new StockOverviewDto { ServerTimeUtc = _clock.UtcNow, Items = rows };
    }
}
