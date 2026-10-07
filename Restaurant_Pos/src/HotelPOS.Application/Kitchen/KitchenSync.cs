using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Floor;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Floor;
using HotelPOS.Domain.Kitchen;
using HotelPOS.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Kitchen;

public sealed record KitchenChange(OrderStatus OrderBefore, OrderStatus OrderAfter, TableStatus TableBefore, TableStatus TableAfter);

public sealed class KitchenSync
{
    private readonly IAppDbContext _db;
    private readonly IRealtimeNotifier _realtime;
    private readonly IClock _clock;
    private readonly TableEvents _tableEvents;

    public KitchenSync(IAppDbContext db, IRealtimeNotifier realtime, IClock clock, TableEvents tableEvents)
    {
        _db = db;
        _realtime = realtime;
        _clock = clock;
        _tableEvents = tableEvents;
    }

    public Task<List<KitchenOrder>> LoadTicketsAsync(int orderId, CancellationToken cancellationToken) =>
        _db.KitchenOrders.Include(k => k.Items).Where(k => k.OrderId == orderId).ToListAsync(cancellationToken);

    public KitchenChange Recompute(Order order, IReadOnlyCollection<KitchenOrder> tickets, Table table)
    {
        var orderBefore = order.Status;
        var tableBefore = table.Status;
        if (OrderStatusDeriver.Derive(tickets.Select(t => t.Status)) is { } derived)
        {
            order.ApplyKitchenStatus(derived, _clock.UtcNow);
        }

        if (table.CurrentOrderId == order.Id || ReferenceEquals(table.CurrentOrder, order))
        {
            table.FollowKitchen(order.Status, tickets.Any(t => t.Status == KitchenOrderStatus.Ready));
        }

        return new KitchenChange(orderBefore, order.Status, tableBefore, table.Status);
    }

    // Call after the commit. At most one event per kind, so a single kitchen tap never floods the terminals.
    public async Task PublishAsync(
        Order order,
        Table table,
        KitchenChange change,
        IEnumerable<KitchenOrder> created,
        IEnumerable<KitchenOrder> updated,
        IReadOnlyCollection<string> newlyReady)
    {
        var now = _clock.UtcNow;
        foreach (var ticket in created)
        {
            await _realtime.PublishAsync(HubEvents.KitchenTicketCreated, new KitchenTicketCreatedEvent
            {
                OccurredAtUtc = now,
                EntityId = ticket.Id,
                EntityVersion = RowVersions.Encode(ticket.RowVersion),
                TicketId = ticket.Id,
                TicketNumber = ticket.TicketNumber,
                OrderId = order.Id,
                TableCode = table.Code,
                StationId = ticket.PreparationStationId,
                ItemCount = ticket.Items.Where(i => !i.IsCancelled).Sum(i => i.Quantity),
            }, new RealtimeAudience { StationIds = new[] { ticket.PreparationStationId }, Roles = new[] { Roles.Kitchen } }, CancellationToken.None);
        }

        foreach (var ticket in updated)
        {
            await _realtime.PublishAsync(HubEvents.KitchenTicketUpdated, new KitchenTicketUpdatedEvent
            {
                OccurredAtUtc = now,
                EntityId = ticket.Id,
                EntityVersion = RowVersions.Encode(ticket.RowVersion),
                TicketId = ticket.Id,
                TicketNumber = ticket.TicketNumber,
                OrderId = order.Id,
                TableCode = table.Code,
                StationId = ticket.PreparationStationId,
                Status = ticket.Status,
            }, new RealtimeAudience
            {
                StationIds = new[] { ticket.PreparationStationId },
                Roles = new[] { Roles.Kitchen, Roles.Manager },
                UserIds = new[] { order.WaiterId },
            }, CancellationToken.None);
        }

        var floorAudience = new RealtimeAudience { Roles = new[] { Roles.Waiter, Roles.Cashier, Roles.Manager }, UserIds = new[] { order.WaiterId } };
        if (newlyReady.Count > 0)
        {
            await PublishProgressAsync(HubEvents.OrderReady, order, table, floorAudience, newlyReady);
        }

        if (change.OrderAfter != change.OrderBefore)
        {
            var eventName = change.OrderAfter switch
            {
                OrderStatus.Accepted => HubEvents.OrderAccepted,
                OrderStatus.Preparing => HubEvents.OrderPreparing,
                OrderStatus.Served => HubEvents.OrderServed,
                _ => null,
            };
            if (eventName is not null)
            {
                await PublishProgressAsync(eventName, order, table, floorAudience, Array.Empty<string>());
            }
            else if (change.OrderAfter != OrderStatus.Ready)
            {
                await _realtime.PublishAsync(HubEvents.OrderUpdated, new OrderUpdatedEvent
                {
                    OccurredAtUtc = now,
                    EntityId = order.Id,
                    EntityVersion = RowVersions.Encode(order.RowVersion),
                    OrderId = order.Id,
                    OrderNumber = order.OrderNumber,
                    Status = order.Status,
                    ChangeType = OrderChangeTypes.StatusDerived,
                }, floorAudience, CancellationToken.None);
            }
        }

        if (change.TableAfter != change.TableBefore)
        {
            await _tableEvents.PublishStatusAsync(table, order.OrderNumber);
        }
    }

    private Task PublishProgressAsync(string eventName, Order order, Table table, RealtimeAudience audience, IReadOnlyCollection<string> readyTickets) =>
        _realtime.PublishAsync(eventName, new OrderProgressEvent
        {
            OccurredAtUtc = _clock.UtcNow,
            EntityId = order.Id,
            EntityVersion = RowVersions.Encode(order.RowVersion),
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            TableId = table.Id,
            TableCode = table.Code,
            WaiterId = order.WaiterId,
            Status = order.Status,
            ReadyTicketNumbers = readyTickets.ToList(),
        }, audience, CancellationToken.None);
}

public sealed class TicketFactory
{
    private readonly IAppDbContext _db;

    public TicketFactory(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<List<KitchenOrder>> CreateAsync(Order order, int batchNumber, CancellationToken cancellationToken)
    {
        var items = order.Items.Where(i => i.BatchNumber == batchNumber && i.Status == OrderItemStatus.Sent).ToList();
        var byStation = items.GroupBy(i => i.PreparationStationId).ToList();
        var stationIds = byStation.Select(g => g.Key).ToList();
        var codes = await _db.PreparationStations.AsNoTracking()
            .Where(s => stationIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Code, cancellationToken);

        var baseNumber = $"{order.OrderNumber}-{batchNumber}";
        return byStation
            .OrderBy(g => codes.GetValueOrDefault(g.Key, string.Empty), StringComparer.Ordinal)
            .Select(g => new KitchenOrder(
                order,
                batchNumber,
                g.Key,
                byStation.Count == 1 ? baseNumber : $"{baseNumber}-{codes.GetValueOrDefault(g.Key, g.Key.ToString())}",
                g))
            .ToList();
    }
}
