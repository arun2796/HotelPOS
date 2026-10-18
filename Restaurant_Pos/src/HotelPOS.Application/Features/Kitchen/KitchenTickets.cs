using System.Globalization;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Kitchen;
using HotelPOS.Contracts.Orders;
using HotelPOS.Domain.Common;
using HotelPOS.Domain.Kitchen;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Kitchen;

// Steps shared by the kitchen ticket commands and queries.
internal sealed class KitchenTickets
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly IAfterCommit _afterCommit;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly KitchenSync _sync;

    public KitchenTickets(IAppDbContext db, IAuditTrail audit, IAfterCommit afterCommit, ICurrentUser currentUser, IClock clock, KitchenSync sync)
    {
        _db = db;
        _audit = audit;
        _afterCommit = afterCommit;
        _currentUser = currentUser;
        _clock = clock;
        _sync = sync;
    }

    public IQueryable<KitchenOrder> Tickets() =>
        _db.KitchenOrders.AsNoTracking()
            .Include(k => k.Order).ThenInclude(o => o!.Table)
            .Include(k => k.Items).ThenInclude(i => i.OrderItem).ThenInclude(oi => oi!.Modifiers);

    public async Task<Result<KitchenTicketDto>> GetAsync(int id, CancellationToken cancellationToken)
    {
        var ticket = await Tickets().FirstOrDefaultAsync(k => k.Id == id, cancellationToken);
        return ticket is null ? AppErrors.NotFound("Ticket", id) : (await ToDtosAsync(new[] { ticket }, cancellationToken))[0];
    }

    public async Task<List<KitchenTicketDto>> ToDtosAsync(IReadOnlyCollection<KitchenOrder> tickets, CancellationToken cancellationToken)
    {
        var waiterIds = tickets.Select(t => t.Order!.WaiterId).Distinct().ToList();
        var waiters = await _db.Users.AsNoTracking().Where(u => waiterIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);
        var stations = await _db.PreparationStations.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Code, cancellationToken);
        return tickets.Select(t => ToDto(t, waiters.GetValueOrDefault(t.Order!.WaiterId, string.Empty), stations.GetValueOrDefault(t.PreparationStationId, string.Empty))).ToList();
    }

    // One step of the ticket state machine; the order and table follow the tickets.
    public async Task<Result<KitchenTicketDto>> ChangeAsync(
        int id,
        string auditAction,
        Action<KitchenOrder, int, DateTime> change,
        CancellationToken cancellationToken)
    {
        var ticket = await _db.KitchenOrders
            .Include(k => k.Order).ThenInclude(o => o!.Table)
            .FirstOrDefaultAsync(k => k.Id == id, cancellationToken);
        if (ticket is null)
        {
            return AppErrors.NotFound("Ticket", id);
        }

        var wasReady = ticket.Status == KitchenOrderStatus.Ready;
        try
        {
            change(ticket, _currentUser.UserId ?? 0, _clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return AppErrors.InvalidState(ex.Message, (await GetAsync(id, cancellationToken)).Value);
        }

        var order = ticket.Order!;
        var table = order.Table!;
        var tickets = await _sync.LoadTicketsAsync(order.Id, cancellationToken);
        var kitchenChange = _sync.Recompute(order, tickets, table);
        _audit.Add(auditAction, "KitchenTicket", ticket.Id.ToString(CultureInfo.InvariantCulture),
            newValues: new { ticket.TicketNumber, ticket.Status, Order = order.OrderNumber });

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return AppErrors.Concurrency("ticket", (await GetAsync(id, cancellationToken)).Value);
        }

        var newlyReady = !wasReady && ticket.Status == KitchenOrderStatus.Ready ? new[] { ticket.TicketNumber } : Array.Empty<string>();
        _afterCommit.Enqueue(() => _sync.PublishAsync(order, table, kitchenChange, Array.Empty<KitchenOrder>(), new[] { ticket }, newlyReady));
        return await GetAsync(id, cancellationToken);
    }

    private static KitchenTicketDto ToDto(KitchenOrder ticket, string waiterName, string stationCode) => new()
    {
        Id = ticket.Id,
        TicketNumber = ticket.TicketNumber,
        OrderId = ticket.OrderId,
        OrderNumber = ticket.Order?.OrderNumber ?? 0,
        BatchNumber = ticket.BatchNumber,
        TableCode = ticket.Order is null
            ? string.Empty
            : OrderChannels.TableLabel(ticket.Order.OrderType, ticket.Order.OrderNumber, ticket.Order.Table?.Code),
        OrderType = ticket.Order?.OrderType ?? OrderType.DineIn,
        CustomerName = ticket.Order?.CustomerName,
        CustomerPhone = ticket.Order?.CustomerPhone,
        DeliveryAddress = ticket.Order?.DeliveryAddress,
        WaiterName = waiterName,
        StationId = ticket.PreparationStationId,
        StationCode = stationCode,
        Status = ticket.Status,
        CreatedAtUtc = ticket.CreatedAt,
        AcceptedAtUtc = ticket.AcceptedAt,
        StartedAtUtc = ticket.StartedAt,
        ReadyAtUtc = ticket.ReadyAt,
        CompletedAtUtc = ticket.CompletedAt,
        CancelledAtUtc = ticket.CancelledAt,
        Items = ticket.Items.OrderBy(i => i.Id).Select(i => new KitchenTicketItemDto
        {
            OrderItemId = i.OrderItemId,
            Name = i.OrderItem?.ItemName ?? string.Empty,
            Quantity = i.Quantity,
            Notes = i.OrderItem?.Notes,
            Modifiers = i.OrderItem?.Modifiers.Select(m => m.Name).ToList() ?? new List<string>(),
            IsCancelled = i.IsCancelled,
        }).ToList(),
        RowVersion = RowVersions.Encode(ticket.RowVersion),
    };
}
