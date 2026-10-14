using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Kitchen;

namespace HotelPOS.Application.Features.Kitchen.Queries;

// Kitchen staff and waiters (the runners): the endpoint gates by role, there is no permission for it.
public sealed record GetKitchenTicketQuery(int Id) : IQuery<KitchenTicketDto>;

internal sealed class GetKitchenTicketHandler : IQueryHandler<GetKitchenTicketQuery, KitchenTicketDto>
{
    private readonly KitchenTickets _tickets;

    public GetKitchenTicketHandler(KitchenTickets tickets)
    {
        _tickets = tickets;
    }

    public Task<Result<KitchenTicketDto>> Handle(GetKitchenTicketQuery request, CancellationToken cancellationToken) =>
        _tickets.GetAsync(request.Id, cancellationToken);
}
