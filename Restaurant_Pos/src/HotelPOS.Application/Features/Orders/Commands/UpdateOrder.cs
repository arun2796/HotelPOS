using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Features.Floor;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Orders;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Orders;

namespace HotelPOS.Application.Features.Orders.Commands;

// Guest count and notes.
public sealed record UpdateOrderCommand(int Id, UpdateOrderRequest Input)
    : ICommand<OrderDetailDto>, IHasInput<UpdateOrderRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.OrdersCreate;
}

public sealed class UpdateOrderRequestValidator : AbstractValidator<UpdateOrderRequest>
{
    public UpdateOrderRequestValidator()
    {
        RuleFor(x => x.GuestCount).InclusiveBetween(FloorLimits.MinGuests, FloorLimits.MaxGuests);
        RuleFor(x => x.Notes).MaximumLength(OrderLimits.NotesMaxLength);
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

internal sealed class UpdateOrderHandler : ICommandHandler<UpdateOrderCommand, OrderDetailDto>
{
    private readonly IAuditTrail _audit;
    private readonly IAfterCommit _afterCommit;
    private readonly OrderSteps _orders;
    private readonly OrderEvents _events;
    private readonly TableEvents _tableEvents;

    public UpdateOrderHandler(IAuditTrail audit, IAfterCommit afterCommit, OrderSteps orders, OrderEvents events, TableEvents tableEvents)
    {
        _audit = audit;
        _afterCommit = afterCommit;
        _orders = orders;
        _events = events;
        _tableEvents = tableEvents;
    }

    public async Task<Result<OrderDetailDto>> Handle(UpdateOrderCommand command, CancellationToken cancellationToken)
    {
        var (id, request) = (command.Id, command.Input);
        var (order, error) = await _orders.LoadForChangeAsync(id, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        if (!RowVersions.Matches(order!.RowVersion, request.RowVersion))
        {
            return await _orders.ConflictAsync(id, cancellationToken);
        }

        if (!OrderStateMachine.IsActive(order.Status))
        {
            return AppErrors.OrderLocked($"Order {order.OrderNumber} is {order.Status} and can no longer be changed.");
        }

        var oldValues = new { order.GuestCount, order.Notes };
        order.UpdateDetails(request.GuestCount, request.Notes);
        var table = order.Table!;
        table.SetGuestCount(order.GuestCount);
        _audit.Add(AuditActions.OrderUpdated, nameof(Order), order.Id.ToString(), oldValues, new { order.GuestCount, order.Notes });

        var result = await _orders.SaveAsync(order, cancellationToken);
        if (result.IsSuccess)
        {
            _afterCommit.Enqueue(() => _events.PublishUpdatedAsync(order, OrderChangeTypes.GuestsChanged));
            _afterCommit.Enqueue(() => _tableEvents.PublishStatusAsync(table, order.OrderNumber));
        }

        return result;
    }
}
