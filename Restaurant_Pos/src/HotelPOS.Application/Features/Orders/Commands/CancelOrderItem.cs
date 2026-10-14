using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Kitchen;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Kitchen;
using HotelPOS.Contracts.Orders;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Kitchen;
using HotelPOS.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Orders.Commands;

// Cancels one line that is already with the kitchen.
public sealed record CancelOrderItemCommand(int Id, int ItemId, CancelOrderItemRequest Input)
    : ICommand<OrderDetailDto>, IHasInput<CancelOrderItemRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.OrdersCreate;
}

public sealed class CancelOrderItemRequestValidator : AbstractValidator<CancelOrderItemRequest>
{
    public CancelOrderItemRequestValidator()
    {
        RuleFor(x => x.Reason).MaximumLength(OrderLimits.ReasonMaxLength);
    }
}

internal sealed class CancelOrderItemHandler : ICommandHandler<CancelOrderItemCommand, OrderDetailDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly IAfterCommit _afterCommit;
    private readonly IClock _clock;
    private readonly OrderSteps _orders;
    private readonly OrderEvents _events;
    private readonly KitchenSync _kitchen;

    public CancelOrderItemHandler(
        IAppDbContext db,
        IAuditTrail audit,
        IAfterCommit afterCommit,
        IClock clock,
        OrderSteps orders,
        OrderEvents events,
        KitchenSync kitchen)
    {
        _db = db;
        _audit = audit;
        _afterCommit = afterCommit;
        _clock = clock;
        _orders = orders;
        _events = events;
        _kitchen = kitchen;
    }

    public async Task<Result<OrderDetailDto>> Handle(CancelOrderItemCommand command, CancellationToken cancellationToken)
    {
        var (id, itemId, request) = (command.Id, command.ItemId, command.Input);
        var order = await _orders.WithItems().FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        var item = order?.Items.FirstOrDefault(i => i.Id == itemId);
        if (order is null || item is null)
        {
            return AppErrors.NotFound("Order item", itemId);
        }

        if (!OrderStateMachine.IsInKitchenBand(order.Status))
        {
            return AppErrors.OrderLocked($"Order {order.OrderNumber} is {order.Status}; reopen the bill to change its items.", await _orders.GetDetailAsync(id, cancellationToken));
        }

        if (item.Status != OrderItemStatus.Sent)
        {
            return AppErrors.InvalidState($"{item.ItemName} is not with the kitchen; change the draft instead.", await _orders.GetDetailAsync(id, cancellationToken));
        }

        var tickets = await _kitchen.LoadTicketsAsync(order.Id, cancellationToken);
        var ticket = tickets.First(t => t.Items.Any(i => i.OrderItemId == itemId));
        var isOwner = await _orders.IsOwnerAsync(order, cancellationToken);
        var mayCancelAny = _orders.MayCancelAny;
        switch (ticket.Status)
        {
            case KitchenOrderStatus.New or KitchenOrderStatus.Accepted when !mayCancelAny && !isOwner:
                return AppErrors.Forbidden("Only the waiter who took this order or a manager can cancel its items.");
            case KitchenOrderStatus.Preparing when !mayCancelAny:
                return AppErrors.Forbidden($"{item.ItemName} is already being prepared. Only a manager can cancel it.");
            case KitchenOrderStatus.Ready or KitchenOrderStatus.Completed or KitchenOrderStatus.Cancelled:
                return AppErrors.InvalidState($"{item.ItemName} is already {ticket.Status} and cannot be cancelled.", await _orders.GetDetailAsync(id, cancellationToken));
        }

        var now = _clock.UtcNow;
        item.CancelSent(_orders.UserId, request.Reason);
        ticket.CancelItem(itemId, now);
        _db.Entry(order).State = EntityState.Modified;
        var table = order.Table!;
        var change = _kitchen.Recompute(order, tickets, table);
        _audit.Add(AuditActions.OrderItemCancelled, nameof(Order), order.Id.ToString(),
            newValues: new { order.OrderNumber, item.ItemName, item.Quantity, Ticket = ticket.TicketNumber, request.Reason });

        var result = await _orders.SaveAsync(order, cancellationToken);
        if (result.IsSuccess)
        {
            _afterCommit.Enqueue(() => _events.PublishUpdatedAsync(order, OrderChangeTypes.ItemCancelled));
            _afterCommit.Enqueue(() => _kitchen.PublishAsync(order, table, change, Array.Empty<KitchenOrder>(), new[] { ticket }, Array.Empty<string>()));
        }

        return result;
    }
}
