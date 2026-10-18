using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Features.Floor;
using HotelPOS.Application.Features.Kitchen;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Orders;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Kitchen;
using HotelPOS.Domain.Orders;

namespace HotelPOS.Application.Features.Orders.Commands;

public sealed record SubmitOrderCommand(int Id) : ICommand<OrderDetailDto>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.OrdersCreate;
}

internal sealed class SubmitOrderHandler : ICommandHandler<SubmitOrderCommand, OrderDetailDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly IAfterCommit _afterCommit;
    private readonly IClock _clock;
    private readonly OrderSteps _orders;
    private readonly OrderEvents _events;
    private readonly TicketFactory _tickets;
    private readonly KitchenSync _kitchen;
    private readonly TableEvents _tableEvents;

    public SubmitOrderHandler(
        IAppDbContext db,
        IAuditTrail audit,
        IAfterCommit afterCommit,
        IClock clock,
        OrderSteps orders,
        OrderEvents events,
        TicketFactory tickets,
        KitchenSync kitchen,
        TableEvents tableEvents)
    {
        _db = db;
        _audit = audit;
        _afterCommit = afterCommit;
        _clock = clock;
        _orders = orders;
        _events = events;
        _tickets = tickets;
        _kitchen = kitchen;
        _tableEvents = tableEvents;
    }

    public async Task<Result<OrderDetailDto>> Handle(SubmitOrderCommand command, CancellationToken cancellationToken)
    {
        var id = command.Id;
        var (order, error) = await _orders.LoadForChangeAsync(id, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        if (order!.Status != OrderStatus.Draft)
        {
            return AppErrors.InvalidState($"Order {order.OrderNumber} was already sent to the kitchen.", await _orders.GetDetailAsync(id, cancellationToken));
        }

        if (order.Items.Count == 0)
        {
            return AppErrors.BusinessRule("Add at least one item before sending the order.");
        }

        order.Submit(_clock.UtcNow);
        var table = order.Table;
        if (table is not null)
        {
            table.FollowOrder(order.Status);
        }
        var tickets = await _tickets.CreateAsync(order, 1, cancellationToken);
        _db.KitchenOrders.AddRange(tickets);
        var tableLabel = OrderChannels.TableLabel(order.OrderType, order.OrderNumber, table?.Code);
        _audit.Add(AuditActions.OrderSubmitted, nameof(Order), order.Id.ToString(),
            newValues: new { order.OrderNumber, Table = tableLabel, Items = order.Items.Count });

        var result = await _orders.SaveAsync(order, cancellationToken);
        if (result.IsSuccess)
        {
            if (table is not null)
            {
                _afterCommit.Enqueue(() => _tableEvents.PublishStatusAsync(table, order.OrderNumber));
            }
            _afterCommit.Enqueue(() => _events.PublishCreatedAsync(order, tableLabel));
            _afterCommit.Enqueue(() => _kitchen.PublishAsync(order, table, new KitchenChange(order.Status, order.Status, table?.Status ?? TableStatus.Available, table?.Status ?? TableStatus.Available),
                tickets, Array.Empty<KitchenOrder>(), Array.Empty<string>()));
        }

        return result;
    }
}
