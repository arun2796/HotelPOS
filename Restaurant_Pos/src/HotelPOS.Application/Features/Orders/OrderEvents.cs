using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Orders;

namespace HotelPOS.Application.Features.Orders;

// Publishes at once: handlers queue these calls on IAfterCommit. Not tied to the request: a client that hung up
// must not stop others from being notified.
internal sealed class OrderEvents
{
    private static readonly RealtimeAudience Watchers = RealtimeAudience.ForRoles(Roles.Kitchen, Roles.Cashier, Roles.Manager, Roles.Admin);

    private readonly IRealtimeNotifier _realtime;
    private readonly IClock _clock;
    private readonly OrderSteps _orders;

    public OrderEvents(IRealtimeNotifier realtime, IClock clock, OrderSteps orders)
    {
        _realtime = realtime;
        _clock = clock;
        _orders = orders;
    }

    public async Task PublishCreatedAsync(Order order, string tableCode) =>
        await _realtime.PublishAsync(HubEvents.OrderCreated, new OrderCreatedEvent
        {
            OccurredAtUtc = _clock.UtcNow,
            EntityId = order.Id,
            EntityVersion = RowVersions.Encode(order.RowVersion),
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            TableCode = tableCode,
            WaiterName = await _orders.WaiterNameAsync(order.WaiterId, CancellationToken.None) ?? string.Empty,
            ItemCount = order.ActiveItems.Sum(i => i.Quantity),
        }, Watchers, CancellationToken.None);

    public Task PublishUpdatedAsync(Order order, string changeType) =>
        _realtime.PublishAsync(HubEvents.OrderUpdated, new OrderUpdatedEvent
        {
            OccurredAtUtc = _clock.UtcNow,
            EntityId = order.Id,
            EntityVersion = RowVersions.Encode(order.RowVersion),
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            Status = order.Status,
            ChangeType = changeType,
        }, WatchersAnd(order.WaiterId), CancellationToken.None);

    public Task PublishCancelledAsync(Order order, string tableCode) =>
        _realtime.PublishAsync(HubEvents.OrderCancelled, new OrderCancelledEvent
        {
            OccurredAtUtc = _clock.UtcNow,
            EntityId = order.Id,
            EntityVersion = RowVersions.Encode(order.RowVersion),
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            TableCode = tableCode,
            Reason = order.CancelReason,
        }, WatchersAnd(order.WaiterId), CancellationToken.None);

    private static RealtimeAudience WatchersAnd(int waiterId) => Watchers with { UserIds = new[] { waiterId } };
}
