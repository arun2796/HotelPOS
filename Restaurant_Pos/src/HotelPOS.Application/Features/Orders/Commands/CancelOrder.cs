using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Features.Floor;
using HotelPOS.Application.Kitchen;
using HotelPOS.Contracts.Orders;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Kitchen;
using HotelPOS.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Orders.Commands;

public sealed record CancelOrderCommand(int Id, CancelOrderRequest Input)
    : ICommand<OrderDetailDto>, IHasInput<CancelOrderRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.OrdersCreate;
}

public sealed class CancelOrderRequestValidator : AbstractValidator<CancelOrderRequest>
{
    public CancelOrderRequestValidator()
    {
        RuleFor(x => x.Reason).MaximumLength(OrderLimits.ReasonMaxLength);
    }
}

internal sealed class CancelOrderHandler : ICommandHandler<CancelOrderCommand, OrderDetailDto>
{
    private readonly IAuditTrail _audit;
    private readonly IAfterCommit _afterCommit;
    private readonly IClock _clock;
    private readonly OrderSteps _orders;
    private readonly OrderEvents _events;
    private readonly KitchenSync _kitchen;
    private readonly TableEvents _tableEvents;

    public CancelOrderHandler(
        IAuditTrail audit,
        IAfterCommit afterCommit,
        IClock clock,
        OrderSteps orders,
        OrderEvents events,
        KitchenSync kitchen,
        TableEvents tableEvents)
    {
        _audit = audit;
        _afterCommit = afterCommit;
        _clock = clock;
        _orders = orders;
        _events = events;
        _kitchen = kitchen;
        _tableEvents = tableEvents;
    }

    public async Task<Result<OrderDetailDto>> Handle(CancelOrderCommand command, CancellationToken cancellationToken)
    {
        var (id, request) = (command.Id, command.Input);
        var order = await _orders.WithItems().Include(o => o.Table).FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null)
        {
            return AppErrors.NotFound("Order", id);
        }

        var isOwner = await _orders.IsOwnerAsync(order, cancellationToken);
        switch (OrderStateMachine.CanCancel(order.Status, _orders.MayCancelAny, isOwner))
        {
            case CancelPermission.NotOwner:
                return AppErrors.Forbidden("Only the waiter who took this order or a manager can cancel it.");
            case CancelPermission.ManagerOnly:
                return AppErrors.Forbidden($"Order {order.OrderNumber} is already being prepared. Only a manager can cancel it.");
            case CancelPermission.NotCancellable:
                return AppErrors.InvalidState($"Order {order.OrderNumber} is {order.Status} and cannot be cancelled.", await _orders.GetDetailAsync(id, cancellationToken));
            case CancelPermission.AllowedWithReason when string.IsNullOrWhiteSpace(request.Reason):
                return AppErrors.Validation("reason", "Enter the reason for cancelling an order that is being prepared.");
        }

        var table = order.Table!;
        var openTickets = (await _kitchen.LoadTicketsAsync(order.Id, cancellationToken)).Where(t => t.IsOpen).ToList();
        foreach (var ticket in openTickets)
        {
            ticket.Cancel(_clock.UtcNow);
        }

        order.Cancel(_orders.UserId, request.Reason, _clock.UtcNow);
        if (table.CurrentOrderId == order.Id)
        {
            table.DetachOrder(order.OpenedOnOccupiedTable);
        }

        _audit.Add(AuditActions.OrderCancelled, nameof(Order), order.Id.ToString(),
            newValues: new { order.OrderNumber, Table = table.Code, order.CancelReason });

        var result = await _orders.SaveAsync(order, cancellationToken);
        if (result.IsSuccess)
        {
            _afterCommit.Enqueue(() => _events.PublishCancelledAsync(order, table.Code));
            _afterCommit.Enqueue(() => _tableEvents.PublishStatusAsync(table));
            _afterCommit.Enqueue(() => _kitchen.PublishAsync(order, table, new KitchenChange(order.Status, order.Status, table.Status, table.Status),
                Array.Empty<KitchenOrder>(), openTickets, Array.Empty<string>()));
        }

        return result;
    }
}
