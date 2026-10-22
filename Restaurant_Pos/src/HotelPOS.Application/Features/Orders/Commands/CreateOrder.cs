using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Features.Floor;
using HotelPOS.Application.Features.Kitchen;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Orders;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Floor;
using HotelPOS.Domain.Kitchen;
using HotelPOS.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Orders.Commands;

// IdempotencyKey: the request's Idempotency-Key when it came through the API, stored on the order (see Order.IdempotencyKey).
public sealed record CreateOrderCommand(CreateOrderRequest Input, Guid? IdempotencyKey = null)
    : ICommand<OrderDetailDto>, IHasInput<CreateOrderRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.OrdersCreate;
}

public sealed class CreateOrderRequestValidator : AbstractValidator<CreateOrderRequest>
{
    public CreateOrderRequestValidator()
    {
        RuleFor(x => x.OrderType).IsInEnum();
        RuleFor(x => x.TableId)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("Select a table.")
            .GreaterThan(0).WithMessage("Select a table.")
            .When(x => x.OrderType == OrderType.DineIn);
        RuleFor(x => x.TableId).Null().When(x => x.OrderType != OrderType.DineIn).WithMessage("Takeaway and delivery orders must not specify a table.");
        RuleFor(x => x.CustomerName).NotEmpty().When(x => x.OrderType == OrderType.Delivery).WithMessage("Customer name is required for delivery.");
        RuleFor(x => x.CustomerPhone).NotEmpty().When(x => x.OrderType == OrderType.Delivery).WithMessage("Customer phone is required for delivery.");
        RuleFor(x => x.DeliveryAddress).NotEmpty().When(x => x.OrderType == OrderType.Delivery).WithMessage("Delivery address is required for delivery.");
        RuleFor(x => x.GuestCount).InclusiveBetween(FloorLimits.MinGuests, FloorLimits.MaxGuests);
        RuleFor(x => x.Notes).MaximumLength(OrderLimits.NotesMaxLength);
        RuleFor(x => x.CustomerName).MaximumLength(OrderLimits.CustomerNameMaxLength);
        RuleFor(x => x.CustomerPhone).MaximumLength(OrderLimits.CustomerPhoneMaxLength);
        RuleFor(x => x.DeliveryAddress).MaximumLength(OrderLimits.DeliveryAddressMaxLength);
        RuleFor(x => x.DeliveryNotes).MaximumLength(OrderLimits.NotesMaxLength);
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

        // The same request again, after its first attempt committed without the client hearing back (timeout during
        // COMMIT, server stopped right after): that order exists, so answer with it instead of creating a second one.
        if (command.IdempotencyKey is { } key)
        {
            var existing = await _db.Orders.AsNoTracking().Where(o => o.IdempotencyKey == key).Select(o => (int?)o.Id).FirstOrDefaultAsync(cancellationToken);
            if (existing is { } existingOrderId)
            {
                return await _orders.GetAsync(existingOrderId, cancellationToken);
            }
        }

        Table? table = null;
        if (request.OrderType == OrderType.DineIn)
        {
            table = await _db.Tables.Include(t => t.CurrentOrder).FirstOrDefaultAsync(t => t.Id == request.TableId, cancellationToken);
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
        var order = request.OrderType == OrderType.DineIn
            ? new Order(table!, _orders.UserId, request.GuestCount, request.Notes)
            : new Order(request.OrderType, _orders.UserId, request.GuestCount, request.Notes,
                request.CustomerName, request.CustomerPhone, request.DeliveryAddress, request.DeliveryNotes);

        order.ReplaceDraftItems(items.Value);
        order.RecordCreationKey(command.IdempotencyKey);
        if (request.Submit)
        {
            order.Submit(now);
        }

        if (table != null)
        {
            table.AttachOrder(order, now);
        }
        _db.Orders.Add(order);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            if (table != null)
            {
                var current = await _db.Tables.AsNoTracking().FirstAsync(t => t.Id == table.Id, cancellationToken);
                return AppErrors.TableNotAvailable(current.Code,
                    current.CurrentOrderId is { } id ? await _orders.SummaryAsync(id, cancellationToken) : TableMapping.ToDto(current));
            }
            throw;
        }

        var tableLabel = OrderChannels.TableLabel(order.OrderType, order.OrderNumber, table?.Code);
        var created = new List<KitchenOrder>();
        if (request.Submit)
        {
            // The tickets and the audit entry need the generated id and number.
            created = await _tickets.CreateAsync(order, 1, cancellationToken);
            _db.KitchenOrders.AddRange(created);
            _audit.Add(AuditActions.OrderSubmitted, nameof(Order), order.Id.ToString(),
                newValues: new { order.OrderNumber, Table = tableLabel, Items = order.Items.Count });
            await _db.SaveChangesAsync(cancellationToken);
        }

        if (table != null)
        {
            _afterCommit.Enqueue(() => _tableEvents.PublishStatusAsync(table, order.OrderNumber));
        }

        if (request.Submit)
        {
            _afterCommit.Enqueue(() => _events.PublishCreatedAsync(order, tableLabel));
            _afterCommit.Enqueue(() => _kitchen.PublishAsync(order, table, new KitchenChange(order.Status, order.Status, table?.Status ?? TableStatus.Available, table?.Status ?? TableStatus.Available),
                created, Array.Empty<KitchenOrder>(), Array.Empty<string>()));
        }

        return await _orders.GetAsync(order.Id, cancellationToken);
    }
}
