using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Floor;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Orders;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Common;
using HotelPOS.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Orders;

public interface IOrderService
{
    Task<Result<OrderDetailDto>> CreateAsync(CreateOrderRequest request, CancellationToken cancellationToken = default);

    Task<Result<OrderDetailDto>> GetAsync(int id, CancellationToken cancellationToken = default);

    Task<PagedResult<OrderSummaryDto>> ListAsync(OrderQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderSummaryDto>> GetActiveAsync(CancellationToken cancellationToken = default);

    Task<Result<OrderDetailDto>> ReplaceItemsAsync(int id, ReplaceOrderItemsRequest request, CancellationToken cancellationToken = default);

    Task<Result<OrderDetailDto>> SubmitAsync(int id, CancellationToken cancellationToken = default);

    Task<Result<OrderDetailDto>> AppendItemsAsync(int id, AppendOrderItemsRequest request, CancellationToken cancellationToken = default);

    Task<Result<OrderDetailDto>> UpdateAsync(int id, UpdateOrderRequest request, CancellationToken cancellationToken = default);

    Task<Result<OrderDetailDto>> CancelAsync(int id, CancelOrderRequest request, CancellationToken cancellationToken = default);
}

public sealed class OrderService : IOrderService
{
    private static readonly OrderStatus[] InactiveStatuses = { OrderStatus.Paid, OrderStatus.Completed, OrderStatus.Cancelled };
    private static readonly RealtimeAudience OrderWatchers = RealtimeAudience.ForRoles(Roles.Kitchen, Roles.Cashier, Roles.Manager, Roles.Admin);

    private readonly IAppDbContext _db;
    private readonly IAuditService _audit;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IRealtimeNotifier _realtime;
    private readonly TableEvents _tableEvents;
    private readonly OrderItemFactory _itemFactory;

    public OrderService(
        IAppDbContext db,
        IAuditService audit,
        ICurrentUser currentUser,
        IClock clock,
        IRealtimeNotifier realtime,
        TableEvents tableEvents,
        OrderItemFactory itemFactory)
    {
        _db = db;
        _audit = audit;
        _currentUser = currentUser;
        _clock = clock;
        _realtime = realtime;
        _tableEvents = tableEvents;
        _itemFactory = itemFactory;
    }

    private int UserId => _currentUser.UserId ?? throw new InvalidOperationException("Orders require a signed-in user.");

    private bool IsManager => _currentUser.IsInRole(Roles.Manager) || _currentUser.IsInRole(Roles.Admin);

    public async Task<Result<OrderDetailDto>> CreateAsync(CreateOrderRequest request, CancellationToken cancellationToken = default)
    {
        var table = await _db.Tables.Include(t => t.CurrentOrder).FirstOrDefaultAsync(t => t.Id == request.TableId, cancellationToken);
        if (table is null)
        {
            return AppErrors.Validation("tableId", "The selected table does not exist.");
        }

        if (table.CurrentOrderId is { } existingId)
        {
            return AppErrors.TableNotAvailable(table.Code, await SummaryAsync(existingId, cancellationToken));
        }

        if (!table.CanTakeNewOrder)
        {
            return AppErrors.TableNotAvailable(table.Code, TableService.ToDto(table));
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
        var order = new Order(table, UserId, request.GuestCount, request.Notes);
        order.ReplaceDraftItems(items.Value);
        if (request.Submit)
        {
            order.Submit(now);
        }

        table.AttachOrder(order, now);
        _db.Orders.Add(order);

        // The audit entry needs the generated order id, so both saves share one transaction.
        await using (var transaction = await _db.Database.BeginTransactionAsync(cancellationToken))
        {
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                var current = await _db.Tables.AsNoTracking().FirstAsync(t => t.Id == table.Id, cancellationToken);
                return AppErrors.TableNotAvailable(current.Code,
                    current.CurrentOrderId is { } id ? await SummaryAsync(id, cancellationToken) : TableService.ToDto(current));
            }

            if (request.Submit)
            {
                _audit.Record(AuditActions.OrderSubmitted, nameof(Order), order.Id.ToString(),
                    newValues: new { order.OrderNumber, Table = table.Code, Items = order.Items.Count });
                await _db.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }

        await _tableEvents.PublishStatusAsync(table, order.OrderNumber);
        if (request.Submit)
        {
            await PublishCreatedAsync(order, table.Code);
        }

        return await GetAsync(order.Id, cancellationToken);
    }

    public async Task<Result<OrderDetailDto>> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var order = await WithItems().AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null)
        {
            return AppErrors.NotFound("Order", id);
        }

        var waiterName = await WaiterNameAsync(order.WaiterId, cancellationToken);
        return ToDetail(order, waiterName, await CanModifyAsync(order, cancellationToken));
    }

    public async Task<PagedResult<OrderSummaryDto>> ListAsync(OrderQuery query, CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, PagedQuery.MaxPageSize);
        var orders = _db.Orders.AsNoTracking();
        if (query.Status is { } status)
        {
            orders = orders.Where(o => o.Status == status);
        }

        if (query.TableId is { } tableId)
        {
            orders = orders.Where(o => o.TableId == tableId);
        }

        if (query.WaiterId is { } waiterId)
        {
            orders = orders.Where(o => o.WaiterId == waiterId);
        }

        if (query.FromUtc is { } from)
        {
            orders = orders.Where(o => o.CreatedAt >= from);
        }

        if (query.ToUtc is { } to)
        {
            orders = orders.Where(o => o.CreatedAt < to);
        }

        var total = await orders.CountAsync(cancellationToken);
        var items = await Summaries(orders.OrderByDescending(o => o.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize))
            .ToListAsync(cancellationToken);
        return new PagedResult<OrderSummaryDto> { Items = items, Page = page, PageSize = pageSize, TotalCount = total };
    }

    public async Task<IReadOnlyList<OrderSummaryDto>> GetActiveAsync(CancellationToken cancellationToken = default) =>
        await Summaries(_db.Orders.AsNoTracking()
                .Where(o => !InactiveStatuses.Contains(o.Status))
                .OrderBy(o => o.SubmittedAt ?? o.CreatedAt))
            .ToListAsync(cancellationToken);

    public async Task<Result<OrderDetailDto>> ReplaceItemsAsync(int id, ReplaceOrderItemsRequest request, CancellationToken cancellationToken = default)
    {
        var (order, error) = await LoadForChangeAsync(id, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        if (!RowVersions.Matches(order!.RowVersion, request.RowVersion))
        {
            return await ConflictAsync(id, cancellationToken);
        }

        if (order.Status != OrderStatus.Draft)
        {
            return AppErrors.InvalidState($"Order {order.OrderNumber} was already sent; add items as a new batch instead.");
        }

        var items = await _itemFactory.CreateAsync(request.Items, cancellationToken);
        if (items.IsFailure)
        {
            return items.Error!;
        }

        order.ReplaceDraftItems(items.Value);
        _db.Entry(order).State = EntityState.Modified;
        return await SaveAsync(order, cancellationToken);
    }

    public async Task<Result<OrderDetailDto>> SubmitAsync(int id, CancellationToken cancellationToken = default)
    {
        var (order, error) = await LoadForChangeAsync(id, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        if (order!.Status != OrderStatus.Draft)
        {
            return AppErrors.InvalidState($"Order {order.OrderNumber} was already sent to the kitchen.", await GetDetailAsync(id, cancellationToken));
        }

        if (order.Items.Count == 0)
        {
            return AppErrors.BusinessRule("Add at least one item before sending the order.");
        }

        order.Submit(_clock.UtcNow);
        order.Table!.FollowOrder(order.Status);
        _audit.Record(AuditActions.OrderSubmitted, nameof(Order), order.Id.ToString(),
            newValues: new { order.OrderNumber, Table = order.Table.Code, Items = order.Items.Count });

        var result = await SaveAsync(order, cancellationToken);
        if (result.IsSuccess)
        {
            await _tableEvents.PublishStatusAsync(order.Table, order.OrderNumber);
            await PublishCreatedAsync(order, order.Table.Code);
        }

        return result;
    }

    public async Task<Result<OrderDetailDto>> AppendItemsAsync(int id, AppendOrderItemsRequest request, CancellationToken cancellationToken = default)
    {
        var (order, error) = await LoadForChangeAsync(id, cancellationToken);
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
        _audit.Record(AuditActions.OrderItemsAppended, nameof(Order), order.Id.ToString(),
            newValues: new { order.OrderNumber, Batch = batch, Items = items.Value.Select(i => new { i.ItemName, i.Quantity }).ToList() });

        var result = await SaveAsync(order, cancellationToken);
        if (result.IsSuccess)
        {
            await PublishUpdatedAsync(order, OrderChangeTypes.ItemsAppended);
        }

        return result;
    }

    public async Task<Result<OrderDetailDto>> UpdateAsync(int id, UpdateOrderRequest request, CancellationToken cancellationToken = default)
    {
        var (order, error) = await LoadForChangeAsync(id, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        if (!RowVersions.Matches(order!.RowVersion, request.RowVersion))
        {
            return await ConflictAsync(id, cancellationToken);
        }

        if (!OrderStateMachine.IsActive(order.Status))
        {
            return AppErrors.OrderLocked($"Order {order.OrderNumber} is {order.Status} and can no longer be changed.");
        }

        var oldValues = new { order.GuestCount, order.Notes };
        order.UpdateDetails(request.GuestCount, request.Notes);
        order.Table!.SetGuestCount(order.GuestCount);
        _audit.Record(AuditActions.OrderUpdated, nameof(Order), order.Id.ToString(), oldValues, new { order.GuestCount, order.Notes });

        var result = await SaveAsync(order, cancellationToken);
        if (result.IsSuccess)
        {
            await PublishUpdatedAsync(order, OrderChangeTypes.GuestsChanged);
            await _tableEvents.PublishStatusAsync(order.Table, order.OrderNumber);
        }

        return result;
    }

    public async Task<Result<OrderDetailDto>> CancelAsync(int id, CancelOrderRequest request, CancellationToken cancellationToken = default)
    {
        var order = await WithItems().Include(o => o.Table).FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null)
        {
            return AppErrors.NotFound("Order", id);
        }

        var isOwner = order.WaiterId == _currentUser.UserId || await AnyWaiterMayEditAsync(cancellationToken);
        switch (OrderStateMachine.CanCancel(order.Status, IsManager, isOwner))
        {
            case CancelPermission.NotOwner:
                return AppErrors.Forbidden("Only the waiter who took this order or a manager can cancel it.");
            case CancelPermission.ManagerOnly:
                return AppErrors.Forbidden($"Order {order.OrderNumber} is already being prepared. Only a manager can cancel it.");
            case CancelPermission.NotCancellable:
                return AppErrors.InvalidState($"Order {order.OrderNumber} is {order.Status} and cannot be cancelled.", await GetDetailAsync(id, cancellationToken));
            case CancelPermission.AllowedWithReason when string.IsNullOrWhiteSpace(request.Reason):
                return AppErrors.Validation("reason", "Enter the reason for cancelling an order that is being prepared.");
        }

        var table = order.Table!;
        order.Cancel(UserId, request.Reason, _clock.UtcNow);
        if (table.CurrentOrderId == order.Id)
        {
            table.DetachOrder(order.OpenedOnOccupiedTable);
        }

        _audit.Record(AuditActions.OrderCancelled, nameof(Order), order.Id.ToString(),
            newValues: new { order.OrderNumber, Table = table.Code, order.CancelReason });

        var result = await SaveAsync(order, cancellationToken);
        if (result.IsSuccess)
        {
            await _realtime.PublishAsync(HubEvents.OrderCancelled, new OrderCancelledEvent
            {
                OccurredAtUtc = _clock.UtcNow,
                EntityId = order.Id,
                EntityVersion = RowVersions.Encode(order.RowVersion),
                OrderId = order.Id,
                OrderNumber = order.OrderNumber,
                TableCode = table.Code,
                Reason = order.CancelReason,
            }, WatchersAnd(order.WaiterId), CancellationToken.None);
            await _tableEvents.PublishStatusAsync(table);
        }

        return result;
    }

    private IQueryable<Order> WithItems() =>
        _db.Orders.Include(o => o.Table).Include(o => o.Items).ThenInclude(i => i.Modifiers);

    private async Task<(Order? Order, AppError? Error)> LoadForChangeAsync(int id, CancellationToken cancellationToken)
    {
        var order = await WithItems().FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null)
        {
            return (null, AppErrors.NotFound("Order", id));
        }

        return await CanModifyAsync(order, cancellationToken)
            ? (order, null)
            : (null, AppErrors.Forbidden($"Order {order.OrderNumber} belongs to another waiter. Ask a manager to change it."));
    }

    private async Task<bool> CanModifyAsync(Order order, CancellationToken cancellationToken) =>
        IsManager || order.WaiterId == _currentUser.UserId || await AnyWaiterMayEditAsync(cancellationToken);

    private async Task<bool> AnyWaiterMayEditAsync(CancellationToken cancellationToken)
    {
        if (!_currentUser.IsInRole(Roles.Waiter))
        {
            return false;
        }

        var value = await _db.Settings.AsNoTracking()
            .Where(s => s.Key == SettingKeys.AllowAnyWaiterToEditOrders)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<Result<OrderDetailDto>> SaveAsync(Order order, CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return await ConflictAsync(order.Id, cancellationToken);
        }
        catch (DomainException ex)
        {
            return new AppError(ex.Code, ex.Message);
        }

        return await GetAsync(order.Id, cancellationToken);
    }

    private async Task<Result<OrderDetailDto>> ConflictAsync(int id, CancellationToken cancellationToken) =>
        AppErrors.Concurrency("order", await GetDetailAsync(id, cancellationToken));

    private async Task<OrderDetailDto?> GetDetailAsync(int id, CancellationToken cancellationToken)
    {
        var detail = await GetAsync(id, cancellationToken);
        return detail.IsSuccess ? detail.Value : null;
    }

    private async Task<OrderSummaryDto?> SummaryAsync(int orderId, CancellationToken cancellationToken) =>
        await Summaries(_db.Orders.AsNoTracking().Where(o => o.Id == orderId)).FirstOrDefaultAsync(cancellationToken);

    private IQueryable<OrderSummaryDto> Summaries(IQueryable<Order> orders) =>
        orders.Select(o => new OrderSummaryDto
        {
            Id = o.Id,
            OrderNumber = o.OrderNumber,
            TableId = o.TableId,
            TableCode = o.Table!.Code,
            WaiterId = o.WaiterId,
            WaiterName = _db.Users.Where(u => u.Id == o.WaiterId).Select(u => u.DisplayName).FirstOrDefault() ?? string.Empty,
            GuestCount = o.GuestCount,
            Status = o.Status,
            ItemCount = o.Items.Where(i => i.Status != OrderItemStatus.Cancelled).Sum(i => i.Quantity),
            ApproxTotal = o.Items.Where(i => i.Status != OrderItemStatus.Cancelled)
                .Sum(i => (i.UnitPrice + i.Modifiers.Sum(m => m.PriceDelta)) * i.Quantity),
            CreatedAtUtc = o.CreatedAt,
            SubmittedAtUtc = o.SubmittedAt,
        });

    private Task<string> WaiterNameAsync(int waiterId, CancellationToken cancellationToken) =>
        _db.Users.Where(u => u.Id == waiterId).Select(u => u.DisplayName).FirstOrDefaultAsync(cancellationToken)!;

    private static OrderDetailDto ToDetail(Order order, string waiterName, bool canModify) => new()
    {
        Id = order.Id,
        OrderNumber = order.OrderNumber,
        TableId = order.TableId,
        TableCode = order.Table?.Code ?? string.Empty,
        WaiterId = order.WaiterId,
        WaiterName = waiterName,
        GuestCount = order.GuestCount,
        Notes = order.Notes,
        Status = order.Status,
        CreatedAtUtc = order.CreatedAt,
        SubmittedAtUtc = order.SubmittedAt,
        CancelledAtUtc = order.CancelledAt,
        CancelReason = order.CancelReason,
        Items = order.Items
            .OrderBy(i => i.BatchNumber)
            .ThenBy(i => i.Id)
            .Select(i => new OrderItemDto
            {
                Id = i.Id,
                BatchNumber = i.BatchNumber,
                MenuItemId = i.MenuItemId,
                ItemName = i.ItemName,
                UnitPrice = i.UnitPrice,
                Quantity = i.Quantity,
                Notes = i.Notes,
                TaxRatePercent = i.TaxRatePercent,
                PreparationStationId = i.PreparationStationId,
                Status = i.Status,
                Modifiers = i.Modifiers.Select(m => new OrderItemModifierDto(m.ModifierOptionId, m.Name, m.PriceDelta)).ToList(),
                LineTotal = i.LineTotal,
            })
            .ToList(),
        ApproxSubtotal = order.ApproxSubtotal,
        CanModify = canModify && OrderStateMachine.IsActive(order.Status),
        RowVersion = RowVersions.Encode(order.RowVersion),
    };

    private async Task PublishCreatedAsync(Order order, string tableCode) =>
        await _realtime.PublishAsync(HubEvents.OrderCreated, new OrderCreatedEvent
        {
            OccurredAtUtc = _clock.UtcNow,
            EntityId = order.Id,
            EntityVersion = RowVersions.Encode(order.RowVersion),
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            TableCode = tableCode,
            WaiterName = await WaiterNameAsync(order.WaiterId, CancellationToken.None) ?? string.Empty,
            ItemCount = order.ActiveItems.Sum(i => i.Quantity),
        }, OrderWatchers, CancellationToken.None);

    private Task PublishUpdatedAsync(Order order, string changeType) =>
        _realtime.PublishAsync(HubEvents.OrderUpdated, new OrderUpdatedEvent
        {
            OccurredAtUtc = _clock.UtcNow,
            EntityId = order.Id,
            EntityVersion = RowVersions.Encode(order.RowVersion),
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            Status = order.Status,
            ChangeType = changeType,
        }, WatchersAnd(order.WaiterId), CancellationToken.None);

    private static RealtimeAudience WatchersAnd(int waiterId) => OrderWatchers with { UserIds = new[] { waiterId } };
}
