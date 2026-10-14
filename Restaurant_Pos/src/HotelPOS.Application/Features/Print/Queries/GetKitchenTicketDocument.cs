using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Print;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Print.Queries;

// The KOT for a station printer. Managers, kitchen and waiters: the endpoint gates by role, there is no permission.
public sealed record GetKitchenTicketDocumentQuery(int TicketId) : IQuery<KitchenTicketDocument>;

internal sealed class GetKitchenTicketDocumentHandler : IQueryHandler<GetKitchenTicketDocumentQuery, KitchenTicketDocument>
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly PrintSources _sources;

    public GetKitchenTicketDocumentHandler(IAppDbContext db, IClock clock, PrintSources sources)
    {
        _db = db;
        _clock = clock;
        _sources = sources;
    }

    public async Task<Result<KitchenTicketDocument>> Handle(GetKitchenTicketDocumentQuery request, CancellationToken cancellationToken)
    {
        var ticketId = request.TicketId;
        var ticket = await _db.KitchenOrders.AsNoTracking()
            .Include(k => k.Order).ThenInclude(o => o!.Table)
            .Include(k => k.Items).ThenInclude(i => i.OrderItem).ThenInclude(oi => oi!.Modifiers)
            .FirstOrDefaultAsync(k => k.Id == ticketId, cancellationToken);
        if (ticket?.Order is null)
        {
            return AppErrors.NotFound("Ticket", ticketId);
        }

        var order = ticket.Order;
        var station = await _db.PreparationStations.AsNoTracking()
            .Where(s => s.Id == ticket.PreparationStationId)
            .Select(s => new { s.Code, s.Name })
            .FirstOrDefaultAsync(cancellationToken);
        var settings = await _sources.SettingsAsync(cancellationToken);
        var showPrices = string.Equals(settings.GetValueOrDefault(SettingKeys.KotShowPrices), "true", StringComparison.OrdinalIgnoreCase);

        return new KitchenTicketDocument
        {
            TicketId = ticket.Id,
            TicketNumber = ticket.TicketNumber,
            OrderNumber = order.OrderNumber,
            BatchNumber = ticket.BatchNumber,
            IsAddition = ticket.BatchNumber > 1,
            TableCode = order.Table?.Code ?? string.Empty,
            WaiterName = await _sources.UserNameAsync(order.WaiterId, cancellationToken),
            StationCode = station?.Code ?? string.Empty,
            StationName = station?.Name ?? string.Empty,
            GuestCount = order.GuestCount,
            OrderNotes = order.Notes,
            CreatedAtUtc = ticket.CreatedAt,
            PrintedAtUtc = _clock.UtcNow,
            ShowPrices = showPrices,
            CurrencySymbol = settings.GetValueOrDefault(SettingKeys.CurrencySymbol) ?? string.Empty,
            Items = ticket.Items.OrderBy(i => i.Id).Select(i => new KotLine
            {
                Name = i.OrderItem?.ItemName ?? string.Empty,
                Quantity = i.Quantity,
                Notes = i.OrderItem?.Notes,
                Modifiers = i.OrderItem?.Modifiers.Select(m => m.Name).ToList() ?? new List<string>(),
                IsCancelled = i.IsCancelled,
                UnitPrice = showPrices ? i.OrderItem?.UnitPrice : null,
            }).ToList(),
        };
    }
}
