using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Features.Kitchen;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Billing;
using HotelPOS.Domain.Floor;
using HotelPOS.Domain.Kitchen;
using HotelPOS.Domain.Orders;

namespace HotelPOS.Application.Features.Billing;

// Publishes at once: handlers queue these calls on IAfterCommit. Not tied to the request: a client that hung up
// must not stop others from being notified.
internal sealed class BillEvents
{
    private static readonly RealtimeAudience CashierDesk = RealtimeAudience.ForRoles(Roles.Cashier, Roles.Manager, Roles.Admin);
    private static readonly RealtimeAudience OrderWatchers = RealtimeAudience.ForRoles(Roles.Kitchen, Roles.Cashier, Roles.Manager, Roles.Admin);

    private readonly IRealtimeNotifier _realtime;
    private readonly IClock _clock;
    private readonly KitchenSync _kitchen;
    private readonly BillSteps _bills;

    public BillEvents(IRealtimeNotifier realtime, IClock clock, KitchenSync kitchen, BillSteps bills)
    {
        _realtime = realtime;
        _clock = clock;
        _kitchen = kitchen;
        _bills = bills;
    }

    // Nothing was saved, so this is not queued after a commit. Only the paying terminal (or its user) hears it.
    public Task PublishPaymentFailedAsync(int billId, AppError error, Guid? deviceId, int? userId)
    {
        var audience = deviceId is { } device
            ? new RealtimeAudience { DeviceIds = new[] { device } }
            : new RealtimeAudience { UserIds = userId is { } user ? new[] { user } : Array.Empty<int>() };
        return _realtime.PublishAsync(HubEvents.PaymentFailed, new PaymentFailedEvent
        {
            OccurredAtUtc = _clock.UtcNow,
            EntityId = billId,
            BillId = billId,
            Code = error.Code,
            Message = error.Message,
        }, audience, CancellationToken.None);
    }

    public async Task PublishRequestedAsync(Bill bill, Order order, string tableCode) =>
        await _realtime.PublishAsync(HubEvents.BillRequested, new BillRequestedEvent
        {
            OccurredAtUtc = _clock.UtcNow,
            EntityId = bill.Id,
            EntityVersion = RowVersions.Encode(bill.RowVersion),
            BillId = bill.Id,
            BillNumber = bill.BillNumber,
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            TableCode = tableCode,
            WaiterName = await _bills.UserNameAsync(order.WaiterId, CancellationToken.None) ?? string.Empty,
            GrandTotal = bill.GrandTotal,
        }, CashierDesk, CancellationToken.None);

    public async Task PublishBillUpdatedAsync(Bill bill, string changeType)
    {
        var claimed = bill.ClaimExpiresAt > _clock.UtcNow;
        await _realtime.PublishAsync(HubEvents.BillUpdated, new BillUpdatedEvent
        {
            OccurredAtUtc = _clock.UtcNow,
            EntityId = bill.Id,
            EntityVersion = RowVersions.Encode(bill.RowVersion),
            BillId = bill.Id,
            OrderId = bill.OrderId,
            TableCode = bill.TableCode,
            Status = bill.Status,
            PaymentStatus = bill.PaymentStatus,
            ClaimedByDevice = claimed ? await _bills.HolderAsync(bill, CancellationToken.None) : null,
            GrandTotal = bill.GrandTotal,
            PaidAmount = bill.PaidAmount,
            ChangeType = changeType,
        }, CashierDesk with { UserIds = new[] { bill.WaiterId } }, CancellationToken.None);
    }

    public Task PublishOrderUpdatedAsync(Order order, string changeType) =>
        _realtime.PublishAsync(HubEvents.OrderUpdated, new OrderUpdatedEvent
        {
            OccurredAtUtc = _clock.UtcNow,
            EntityId = order.Id,
            EntityVersion = RowVersions.Encode(order.RowVersion),
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            Status = order.Status,
            ChangeType = changeType,
        }, OrderWatchers with { UserIds = new[] { order.WaiterId } }, CancellationToken.None);

    public Task PublishOrderCancelledAsync(Order order, string tableCode) =>
        _realtime.PublishAsync(HubEvents.OrderCancelled, new OrderCancelledEvent
        {
            OccurredAtUtc = _clock.UtcNow,
            EntityId = order.Id,
            EntityVersion = RowVersions.Encode(order.RowVersion),
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            TableCode = tableCode,
            Reason = order.CancelReason,
        }, OrderWatchers with { UserIds = new[] { order.WaiterId } }, CancellationToken.None);

    public Task PublishPaymentCompletedAsync(Bill bill, Order order, Table table) =>
        _realtime.PublishAsync(HubEvents.PaymentCompleted, new PaymentCompletedEvent
        {
            OccurredAtUtc = _clock.UtcNow,
            EntityId = bill.Id,
            EntityVersion = RowVersions.Encode(bill.RowVersion),
            BillId = bill.Id,
            OrderId = order.Id,
            TableId = table.Id,
            TableCode = table.Code,
            InvoiceNumber = bill.InvoiceNumber,
            GrandTotal = bill.GrandTotal,
        }, RealtimeAudience.ForRoles(Roles.Cashier, Roles.Waiter, Roles.Manager, Roles.Admin), CancellationToken.None);

    public Task PublishTicketsAsync(Order order, Table table, IReadOnlyCollection<KitchenOrder> tickets) =>
        tickets.Count == 0
            ? Task.CompletedTask
            : _kitchen.PublishAsync(order, table, new KitchenChange(order.Status, order.Status, table.Status, table.Status),
                Array.Empty<KitchenOrder>(), tickets, Array.Empty<string>());
}
