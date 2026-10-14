using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Orders;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Orders.Queries;

// Readable by every signed-in role (the endpoint has no permission policy).
public sealed record ListActiveOrdersQuery : IQuery<IReadOnlyList<OrderSummaryDto>>;

internal sealed class ListActiveOrdersHandler : IQueryHandler<ListActiveOrdersQuery, IReadOnlyList<OrderSummaryDto>>
{
    private static readonly OrderStatus[] InactiveStatuses = { OrderStatus.Paid, OrderStatus.Completed, OrderStatus.Cancelled };

    private readonly IAppDbContext _db;
    private readonly OrderSteps _orders;

    public ListActiveOrdersHandler(IAppDbContext db, OrderSteps orders)
    {
        _db = db;
        _orders = orders;
    }

    public async Task<Result<IReadOnlyList<OrderSummaryDto>>> Handle(ListActiveOrdersQuery request, CancellationToken cancellationToken) =>
        await _orders.Summaries(_db.Orders.AsNoTracking()
                .Where(o => !InactiveStatuses.Contains(o.Status))
                .OrderBy(o => o.SubmittedAt ?? o.CreatedAt))
            .ToListAsync(cancellationToken);
}
