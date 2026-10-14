using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Kitchen;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Orders;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Kitchen;
using HotelPOS.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Orders.Commands;

// A new batch for an order that is already with the kitchen.
public sealed record AppendOrderItemsCommand(int Id, AppendOrderItemsRequest Input)
    : ICommand<OrderDetailDto>, IHasInput<AppendOrderItemsRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.OrdersCreate;
}

public sealed class AppendOrderItemsRequestValidator : AbstractValidator<AppendOrderItemsRequest>
{
    public AppendOrderItemsRequestValidator()
    {
        RuleFor(x => x.Items).NotEmpty().WithMessage("Add at least one item.").Must(i => i.Count <= OrderLimits.MaxItemsPerRequest);
        RuleForEach(x => x.Items).SetValidator(new OrderItemInputValidator());
    }
}

internal sealed class AppendOrderItemsHandler : ICommandHandler<AppendOrderItemsCommand, OrderDetailDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly IAfterCommit _afterCommit;
    private readonly OrderSteps _orders;
    private readonly OrderEvents _events;
    private readonly OrderItemFactory _itemFactory;
    private readonly TicketFactory _tickets;
    private readonly KitchenSync _kitchen;

    public AppendOrderItemsHandler(
        IAppDbContext db,
        IAuditTrail audit,
        IAfterCommit afterCommit,
        OrderSteps orders,
        OrderEvents events,
        OrderItemFactory itemFactory,
        TicketFactory tickets,
        KitchenSync kitchen)
    {
        _db = db;
        _audit = audit;
        _afterCommit = afterCommit;
        _orders = orders;
        _events = events;
        _itemFactory = itemFactory;
        _tickets = tickets;
        _kitchen = kitchen;
    }

    public async Task<Result<OrderDetailDto>> Handle(AppendOrderItemsCommand command, CancellationToken cancellationToken)
    {
        var (id, request) = (command.Id, command.Input);
        var (order, error) = await _orders.LoadForChangeAsync(id, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        if (order!.Status == OrderStatus.Draft)
        {
            return AppErrors.InvalidState("The order is still a draft: change its items instead.");
        }

        if (!OrderStateMachine.IsInKitchenBand(order.Status))
        {
            return AppErrors.OrderLocked($"Order {order.OrderNumber} is {order.Status} and can no longer take items.");
        }

        var items = await _itemFactory.CreateAsync(request.Items, cancellationToken);
        if (items.IsFailure)
        {
            return items.Error!;
        }

        var batch = order.AppendBatch(items.Value);
        _db.Entry(order).State = EntityState.Modified;
        var existing = await _kitchen.LoadTicketsAsync(order.Id, cancellationToken);
        var created = await _tickets.CreateAsync(order, batch, cancellationToken);
        _db.KitchenOrders.AddRange(created);
        var table = order.Table!;
        var change = _kitchen.Recompute(order, existing.Concat(created).ToList(), table);
        _audit.Add(AuditActions.OrderItemsAppended, nameof(Order), order.Id.ToString(),
            newValues: new { order.OrderNumber, Batch = batch, Items = items.Value.Select(i => new { i.ItemName, i.Quantity }).ToList() });

        var result = await _orders.SaveAsync(order, cancellationToken);
        if (result.IsSuccess)
        {
            _afterCommit.Enqueue(() => _events.PublishUpdatedAsync(order, OrderChangeTypes.ItemsAppended));
            _afterCommit.Enqueue(() => _kitchen.PublishAsync(order, table, change, created, Array.Empty<KitchenOrder>(), Array.Empty<string>()));
        }

        return result;
    }
}
