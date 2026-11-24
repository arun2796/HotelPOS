using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Menu.Queries;

// The stock ledger of one item, newest first, with today's sold / refilled / wasted totals.
public sealed record GetStockHistoryQuery(int MenuItemId, int Take = 100) : IQuery<StockHistoryDto>, IRequirePermission
{
    public string Permission => Permissions.MenuView;
}

internal sealed class GetStockHistoryHandler : IQueryHandler<GetStockHistoryQuery, StockHistoryDto>
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;

    public GetStockHistoryHandler(IAppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result<StockHistoryDto>> Handle(GetStockHistoryQuery request, CancellationToken cancellationToken)
    {
        var item = await _db.MenuItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == request.MenuItemId, cancellationToken);
        if (item is null)
        {
            return AppErrors.NotFound("Menu item", request.MenuItemId);
        }

        var take = Math.Clamp(request.Take, 1, 500);
        var movements = await _db.StockMovements.AsNoTracking()
            .Where(m => m.MenuItemId == item.Id)
            .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
            .Take(take)
            .Select(m => new StockMovementDto
            {
                Id = m.Id,
                MenuItemId = m.MenuItemId,
                ItemName = item.Name,
                Type = m.Type,
                PreviousQuantity = m.PreviousQuantity,
                QuantityChanged = m.QuantityChanged,
                NewQuantity = m.NewQuantity,
                OrderId = m.OrderId,
                OrderNumber = m.Order == null ? null : m.Order.OrderNumber,
                UserName = _db.Users.Where(u => u.Id == m.CreatedBy).Select(u => u.DisplayName).FirstOrDefault(),
                AtUtc = m.CreatedAt,
                Remarks = m.Remarks,
            })
            .ToListAsync(cancellationToken);

        var (from, to) = await _db.BusinessDayAsync(_clock, null, cancellationToken);
        var today = await _db.StockMovements.AsNoTracking()
            .Where(m => m.MenuItemId == item.Id && m.CreatedAt >= from && m.CreatedAt < to)
            .GroupBy(m => m.Type)
            .Select(g => new { Type = g.Key, Total = g.Sum(m => m.QuantityChanged) })
            .ToListAsync(cancellationToken);
        int Sum(StockMovementType type) => today.Where(t => t.Type == type).Sum(t => t.Total);

        return new StockHistoryDto
        {
            MenuItemId = item.Id,
            ItemName = item.Name,
            Remaining = item.StockRemaining,
            Sold = -(Sum(StockMovementType.OrderReservation) + Sum(StockMovementType.OrderRelease)),
            Refilled = Sum(StockMovementType.Refill),
            Wasted = -Sum(StockMovementType.Waste),
            Movements = movements,
        };
    }
}
