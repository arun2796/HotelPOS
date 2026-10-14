using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Kitchen;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Kitchen.Queries;

// The open tickets of the kitchen display, or those of one status, optionally for one station.
public sealed record ListKitchenTicketsQuery(KitchenTicketQuery Input)
    : IQuery<KitchenTicketListDto>, IHasInput<KitchenTicketQuery>, IRequirePermission
{
    public string Permission => Permissions.KitchenOperate;
}

internal sealed class ListKitchenTicketsHandler : IQueryHandler<ListKitchenTicketsQuery, KitchenTicketListDto>
{
    private static readonly KitchenOrderStatus[] OpenStatuses =
    {
        KitchenOrderStatus.New, KitchenOrderStatus.Accepted, KitchenOrderStatus.Preparing, KitchenOrderStatus.Ready,
    };

    private readonly KitchenTickets _tickets;
    private readonly IClock _clock;

    public ListKitchenTicketsHandler(KitchenTickets tickets, IClock clock)
    {
        _tickets = tickets;
        _clock = clock;
    }

    public async Task<Result<KitchenTicketListDto>> Handle(ListKitchenTicketsQuery request, CancellationToken cancellationToken)
    {
        var query = request.Input;
        var serverTime = _clock.UtcNow;
        var tickets = _tickets.Tickets();
        tickets = query.Status is { } status ? tickets.Where(k => k.Status == status) : tickets.Where(k => OpenStatuses.Contains(k.Status));
        if (query.StationId is { } stationId)
        {
            tickets = tickets.Where(k => k.PreparationStationId == stationId);
        }

        return new KitchenTicketListDto
        {
            ServerTimeUtc = serverTime,
            Tickets = await _tickets.ToDtosAsync(await tickets.OrderBy(k => k.CreatedAt).ToListAsync(cancellationToken), cancellationToken),
        };
    }
}
