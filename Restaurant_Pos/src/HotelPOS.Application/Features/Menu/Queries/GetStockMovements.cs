using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Menu.Queries;

// The stock ledger across all items (the manager's wastage, adjustment and staff-wise report): filter by type, user and
// day, newest first. Without a day it is the current business day.
public sealed record GetStockMovementsQuery(StockMovementType? Type, int? UserId, DateOnly? Day, int Take = 200)
    : IQuery<IReadOnlyList<StockMovementDto>>, IRequirePermission
{
    public string Permission => Permissions.ReportsView;
}

internal sealed class GetStockMovementsHandler : IQueryHandler<GetStockMovementsQuery, IReadOnlyList<StockMovementDto>>
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;

    public GetStockMovementsHandler(IAppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<StockMovementDto>>> Handle(GetStockMovementsQuery request, CancellationToken cancellationToken)
    {
        var (from, to) = await _db.BusinessDayAsync(_clock, request.Day, cancellationToken);
        var query = _db.StockMovements.AsNoTracking().Where(m => m.CreatedAt >= from && m.CreatedAt < to);
        if (request.Type is { } type)
        {
            query = query.Where(m => m.Type == type);
        }

        if (request.UserId is { } userId)
        {
            query = query.Where(m => m.CreatedBy == userId);
        }

        var rows = await query
            .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
            .Take(Math.Clamp(request.Take, 1, 500))
            .Select(m => new StockMovementDto
            {
                Id = m.Id,
                MenuItemId = m.MenuItemId,
                ItemName = _db.MenuItems.Where(i => i.Id == m.MenuItemId).Select(i => i.Name).FirstOrDefault() ?? string.Empty,
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
        return rows;
    }
}
