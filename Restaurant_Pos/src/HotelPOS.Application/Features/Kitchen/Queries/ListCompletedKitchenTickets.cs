using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Kitchen;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Kitchen.Queries;

// Completed and cancelled tickets of a business day (today when no day is given).
public sealed record ListCompletedKitchenTicketsQuery(DateOnly? BusinessDay) : IQuery<KitchenTicketListDto>, IRequirePermission
{
    public string Permission => Permissions.KitchenOperate;
}

internal sealed class ListCompletedKitchenTicketsHandler : IQueryHandler<ListCompletedKitchenTicketsQuery, KitchenTicketListDto>
{
    private readonly IAppDbContext _db;
    private readonly KitchenTickets _tickets;
    private readonly IClock _clock;

    public ListCompletedKitchenTicketsHandler(IAppDbContext db, KitchenTickets tickets, IClock clock)
    {
        _db = db;
        _tickets = tickets;
        _clock = clock;
    }

    public async Task<Result<KitchenTicketListDto>> Handle(ListCompletedKitchenTicketsQuery request, CancellationToken cancellationToken)
    {
        var (from, to) = await _db.BusinessDayAsync(_clock, request.BusinessDay, cancellationToken);
        var tickets = await _tickets.Tickets()
            .Where(k => (k.Status == KitchenOrderStatus.Completed && k.CompletedAt >= from && k.CompletedAt < to)
                || (k.Status == KitchenOrderStatus.Cancelled && k.CancelledAt >= from && k.CancelledAt < to))
            .OrderByDescending(k => k.CompletedAt ?? k.CancelledAt)
            .ToListAsync(cancellationToken);
        return new KitchenTicketListDto { ServerTimeUtc = _clock.UtcNow, Tickets = await _tickets.ToDtosAsync(tickets, cancellationToken) };
    }
}
