using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Features.Floor;
using HotelPOS.Application.Kitchen;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Orders;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Kitchen;
using HotelPOS.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Orders.Commands;

public sealed record CreateOrderCommand(CreateOrderRequest Input)
    : ICommand<OrderDetailDto>, IHasInput<CreateOrderRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.OrdersCreate;
}

public sealed class CreateOrderRequestValidator : AbstractValidator<CreateOrderRequest>
{
    public CreateOrderRequestValidator()
    {
        RuleFor(x => x.TableId).GreaterThan(0).WithMessage("Select a table.");
        RuleFor(x => x.GuestCount).InclusiveBetween(FloorLimits.MinGuests, FloorLimits.MaxGuests);
        RuleFor(x => x.Notes).MaximumLength(OrderLimits.NotesMaxLength);
        RuleFor(x => x.Items).NotNull().Must(i => i.Count <= OrderLimits.MaxItemsPerRequest);
        RuleForEach(x => x.Items).SetValidator(new OrderItemInputValidator());
    }
}

internal sealed class CreateOrderHandler : ICommandHandler<CreateOrderCommand, OrderDetailDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly IAfterCommit _afterCommit;
    private readonly IClock _clock;
    private readonly OrderSteps _orders;
    private readonly OrderEvents _events;
    private readonly OrderItemFactory _itemFactory;
    private readonly TicketFactory _tickets;
    private readonly KitchenSync _kitchen;
    private readonly TableEvents _tableEvents;

    public CreateOrderHandler(
        IAppDbContext db,
        IAuditTrail audit,
        IAfterCommit afterCommit,
        IClock clock,
        OrderSteps orders,
        OrderEvents events,
        OrderItemFactory itemFactory,
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
        _itemFactory = itemFactory;
        _tickets = tickets;
        _kitchen = kitchen;
        _tableEvents = tableEvents;
    }

    public async Task<Result<OrderDetailDto>> Handle(CreateOrderCommand command, CancellationToken cancellationToken)
    {
        var request = command.Input;
        var table = await _db.Tables.Include(t => t.CurrentOrder).FirstOrDefaultAsync(t => t.Id == request.TableId, cancellationToken);
        if (table is null)
        {
            return AppErrors.Validation("tableId", "The selected table does not exist.");
        }

        if (table.CurrentOrderId is { } existingId)
        {
            return AppErrors.TableNotAvailable(table.Code, await _orders.SummaryAsync(existingId, cancellationToken));
        }

        if (!table.CanTakeNewOrder)
        {
            return AppErrors.TableNotAvailable(table.Code, TableMapping.ToDto(table));
        }

        var items = await _itemFactory.CreateAsync(request.Items, cancellationToken);
        if (items.IsFailure)
        {
            return items.Error!;
        }

        if (request.Submit && items.Value.Count == 0)
        {
            return AppErrors.BusinessRule("Add at least one item before sending the order.");
        }

        var now = _clock.UtcNow;
        var order = new Order(table, _orders.UserId, request.GuestCount, request.Notes);
        order.ReplaceDraftItems(items.Value);
        if (request.Submit)
        {
            order.Submit(now);
        }

        table.AttachOrder(order, now);
        _db.Orders.Add(order);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            var current = await _db.Tables.AsNoTracking().FirstAsync(t => t.Id == table.Id, cancellationToken);
            return AppErrors.TableNotAvailable(current.Code,
                current.CurrentOrderId is { } id ? await _orders.SummaryAsync(id, cancellationToken) : TableMapping.ToDto(current));
        }

        var created = new List<KitchenOrder>();
        if (request.Submit)
        {
            // The tickets and the audit entry need the generated id and number.
            created = await _tickets.CreateAsync(order, 1, cancellationToken);
            _db.KitchenOrders.AddRange(created);
            _audit.Add(AuditActions.OrderSubmitted, nameof(Order), order.Id.ToString(),
                newValues: new { order.OrderNumber, Table = table.Code, Items = order.Items.Count });
            await _db.SaveChangesAsync(cancellationToken);
        }

        _afterCommit.Enqueue(() => _tableEvents.PublishStatusAsync(table, order.OrderNumber));
        if (request.Submit)
        {
            _afterCommit.Enqueue(() => _events.PublishCreatedAsync(order, table.Code));
            _afterCommit.Enqueue(() => _kitchen.PublishAsync(order, table, new KitchenChange(order.Status, order.Status, table.Status, table.Status),
                created, Array.Empty<KitchenOrder>(), Array.Empty<string>()));
        }

        return await _orders.GetAsync(order.Id, cancellationToken);
    }
}
