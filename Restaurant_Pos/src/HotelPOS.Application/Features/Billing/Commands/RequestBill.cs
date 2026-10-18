using System.Globalization;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Features.Floor;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Orders;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Domain.Billing;
using HotelPOS.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Billing.Commands;

// Sends an order's bill to the counter. Waiters and the cashier desk: the endpoint gates by role, there is no
// permission for it; a waiter may only request the bill of their own order unless any waiter may edit orders.
public sealed record RequestBillCommand(int OrderId) : ICommand<BillDetailDto>, IAuditedCommand, IBusinessDayGuarded
{
    public BusinessDayScope BusinessDayScope => BusinessDayScope.Today;
}

internal sealed class RequestBillHandler : ICommandHandler<RequestBillCommand, BillDetailDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly IAfterCommit _afterCommit;
    private readonly IClock _clock;
    private readonly BillSteps _bills;
    private readonly BillEvents _events;
    private readonly TableEvents _tableEvents;

    public RequestBillHandler(
        IAppDbContext db,
        IAuditTrail audit,
        IAfterCommit afterCommit,
        IClock clock,
        BillSteps bills,
        BillEvents events,
        TableEvents tableEvents)
    {
        _db = db;
        _audit = audit;
        _afterCommit = afterCommit;
        _clock = clock;
        _bills = bills;
        _events = events;
        _tableEvents = tableEvents;
    }

    public async Task<Result<BillDetailDto>> Handle(RequestBillCommand command, CancellationToken cancellationToken)
    {
        var orderId = command.OrderId;
        var order = await _db.Orders.Include(o => o.Table).Include(o => o.Items).ThenInclude(i => i.Modifiers)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
        if (order is null)
        {
            return AppErrors.NotFound("Order", orderId);
        }

        if (_bills.IsWaiterOnly && order.WaiterId != _bills.UserId && !await _db.BoolSettingAsync(SettingKeys.AllowAnyWaiterToEditOrders, false, cancellationToken))
        {
            return AppErrors.Forbidden($"Order {order.OrderNumber} belongs to another waiter. Ask them or a manager to request the bill.");
        }

        var existing = await _db.Bills.AsNoTracking()
            .Where(b => b.OrderId == orderId && b.Status != BillStatus.Voided)
            .Select(b => (int?)b.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is { } existingId)
        {
            return AppErrors.InvalidState($"The bill for order {order.OrderNumber} was already requested.", await _bills.DetailAsync(existingId, cancellationToken));
        }

        var table = order.Table;
        var tableCode = OrderChannels.TableLabel(order.OrderType, order.OrderNumber, table?.Code);
        var now = _clock.UtcNow;
        Bill bill;
        try
        {
            order.RequestBill(now, await _db.BoolSettingAsync(SettingKeys.AllowBillBeforeReady, false, cancellationToken));
            var lines = order.ActiveItems
                .OrderBy(i => i.BatchNumber).ThenBy(i => i.Id)
                .Select(i => new BillLineInput(i.Id, i.ItemName, i.Quantity, i.UnitPrice, i.Modifiers.Sum(m => m.PriceDelta), i.TaxRatePercent))
                .ToList();
            bill = new Bill(order, tableCode, lines, await _bills.RoundOffAsync(cancellationToken));
        }
        catch (DomainException ex)
        {
            return new AppError(ex.Code, ex.Message);
        }

        if (table != null && table.CurrentOrderId == order.Id)
        {
            table.FollowOrder(order.Status);
        }

        _db.Bills.Add(bill);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return AppErrors.Concurrency("order");
        }

        _audit.Add(AuditActions.BillRequested, nameof(Bill), bill.Id.ToString(CultureInfo.InvariantCulture),
            newValues: new { bill.BillNumber, order.OrderNumber, Table = tableCode, bill.GrandTotal });
        _afterCommit.Enqueue(() => _events.PublishRequestedAsync(bill, order, tableCode));
        if (table != null)
        {
            _afterCommit.Enqueue(() => _tableEvents.PublishStatusAsync(table, order.OrderNumber));
        }
        _afterCommit.Enqueue(() => _events.PublishOrderUpdatedAsync(order, OrderChangeTypes.BillRequested));
        return await _bills.GetAsync(bill.Id, cancellationToken);
    }
}
