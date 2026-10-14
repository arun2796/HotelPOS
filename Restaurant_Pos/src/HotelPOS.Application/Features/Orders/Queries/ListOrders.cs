using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Orders;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Orders.Queries;

// Readable by every signed-in role (the endpoint has no permission policy).
public sealed record ListOrdersQuery(OrderQuery Input) : IQuery<PagedResult<OrderSummaryDto>>, IHasInput<OrderQuery>;

public sealed class OrderQueryValidator : AbstractValidator<OrderQuery>
{
    public OrderQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, OrderQuery.MaxPageSize);
    }
}

internal sealed class ListOrdersHandler : IQueryHandler<ListOrdersQuery, PagedResult<OrderSummaryDto>>
{
    private readonly IAppDbContext _db;
    private readonly OrderSteps _orders;

    public ListOrdersHandler(IAppDbContext db, OrderSteps orders)
    {
        _db = db;
        _orders = orders;
    }

    public async Task<Result<PagedResult<OrderSummaryDto>>> Handle(ListOrdersQuery request, CancellationToken cancellationToken)
    {
        var query = request.Input;
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, PagedQuery.MaxPageSize);
        var orders = _db.Orders.AsNoTracking();
        if (query.Status is { } status)
        {
            orders = orders.Where(o => o.Status == status);
        }

        if (query.TableId is { } tableId)
        {
            orders = orders.Where(o => o.TableId == tableId);
        }

        if (query.WaiterId is { } waiterId)
        {
            orders = orders.Where(o => o.WaiterId == waiterId);
        }

        if (query.FromUtc is { } from)
        {
            orders = orders.Where(o => o.CreatedAt >= from);
        }

        if (query.ToUtc is { } to)
        {
            orders = orders.Where(o => o.CreatedAt < to);
        }

        var total = await orders.CountAsync(cancellationToken);
        var items = await _orders.Summaries(orders.OrderByDescending(o => o.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize))
            .ToListAsync(cancellationToken);
        return new PagedResult<OrderSummaryDto> { Items = items, Page = page, PageSize = pageSize, TotalCount = total };
    }
}
