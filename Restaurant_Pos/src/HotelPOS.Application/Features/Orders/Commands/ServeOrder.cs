using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Features.Kitchen;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Orders;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Kitchen;
using HotelPOS.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Orders.Commands;

// The waiter takes every ready ticket of the order to the table.
public sealed record ServeOrderCommand(int Id) : ICommand<OrderDetailDto>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.OrdersCreate;
}

internal sealed class ServeOrderHandler : ICommandHandler<ServeOrderCommand, OrderDetailDto>
{
    private readonly IAuditTrail _audit;
    private readonly IAfterCommit _afterCommit;
    private readonly IClock _clock;
    private readonly OrderSteps _orders;
    private readonly KitchenSync _kitchen;

    public ServeOrderHandler(IAuditTrail audit, IAfterCommit afterCommit, IClock clock, OrderSteps orders, KitchenSync kitchen)
    {
        _audit = audit;
        _afterCommit = afterCommit;
        _clock = clock;
        _orders = orders;
        _kitchen = kitchen;
    }

    public async Task<Result<OrderDetailDto>> Handle(ServeOrderCommand command, CancellationToken cancellationToken)
    {
        var id = command.Id;
        var order = await _orders.WithItems().FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null)
        {
            return AppErrors.NotFound("Order", id);
        }

        var tickets = await _kitchen.LoadTicketsAsync(order.Id, cancellationToken);
        var ready = tickets.Where(t => t.Status == KitchenOrderStatus.Ready).ToList();
        if (ready.Count == 0)
        {
            return AppErrors.InvalidState($"Nothing of order {order.OrderNumber} is ready to serve.", await _orders.GetDetailAsync(id, cancellationToken));
        }

        var now = _clock.UtcNow;
        foreach (var ticket in ready)
        {
            ticket.Complete(_orders.UserId, now);
        }

        var table = order.Table!;
        var change = _kitchen.Recompute(order, tickets, table);
        _audit.Add(AuditActions.OrderServed, nameof(Order), order.Id.ToString(),
            newValues: new { order.OrderNumber, Tickets = ready.Select(t => t.TicketNumber).ToList(), order.Status });

        var result = await _orders.SaveAsync(order, cancellationToken);
        if (result.IsSuccess)
        {
            _afterCommit.Enqueue(() => _kitchen.PublishAsync(order, table, change, Array.Empty<KitchenOrder>(), ready, Array.Empty<string>()));
        }

        return result;
    }
}
