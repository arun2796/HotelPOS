using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Orders;

namespace HotelPOS.Application.Features.Orders.Queries;

// Readable by every signed-in role (the endpoint has no permission policy).
public sealed record GetOrderQuery(int Id) : IQuery<OrderDetailDto>;

internal sealed class GetOrderHandler : IQueryHandler<GetOrderQuery, OrderDetailDto>
{
    private readonly OrderSteps _orders;

    public GetOrderHandler(OrderSteps orders)
    {
        _orders = orders;
    }

    public Task<Result<OrderDetailDto>> Handle(GetOrderQuery request, CancellationToken cancellationToken) =>
        _orders.GetAsync(request.Id, cancellationToken);
}
