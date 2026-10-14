using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Orders;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Common;
using HotelPOS.Domain.Kitchen;
using HotelPOS.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Orders;

// Steps shared by the order commands and queries.
internal sealed class OrderSteps
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public OrderSteps(IAppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public int UserId => _currentUser.UserId ?? throw new InvalidOperationException("Orders require a signed-in user.");

    public bool MayCancelAny => _currentUser.HasPermission(Permissions.OrdersCancelAny);

    private bool MayEditAny => _currentUser.HasPermission(Permissions.OrdersEditAny);

    public IQueryable<Order> WithItems() =>
        _db.Orders.Include(o => o.Table).Include(o => o.Items).ThenInclude(i => i.Modifiers);

    public async Task<(Order? Order, AppError? Error)> LoadForChangeAsync(int id, CancellationToken cancellationToken)
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

    public async Task<bool> IsOwnerAsync(Order order, CancellationToken cancellationToken) =>
        order.WaiterId == _currentUser.UserId || await AnyWaiterMayEditAsync(cancellationToken);

    public async Task<Result<OrderDetailDto>> SaveAsync(Order order, CancellationToken cancellationToken)
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

    public async Task<Result<OrderDetailDto>> ConflictAsync(int id, CancellationToken cancellationToken) =>
        AppErrors.Concurrency("order", await GetDetailAsync(id, cancellationToken));

    public async Task<OrderDetailDto?> GetDetailAsync(int id, CancellationToken cancellationToken)
    {
        var detail = await GetAsync(id, cancellationToken);
        return detail.IsSuccess ? detail.Value : null;
    }

    public async Task<Result<OrderDetailDto>> GetAsync(int id, CancellationToken cancellationToken)
    {
        var order = await WithItems().AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null)
        {
            return AppErrors.NotFound("Order", id);
        }

        var waiterName = await WaiterNameAsync(order.WaiterId, cancellationToken);
        var tickets = await _db.KitchenOrders.AsNoTracking().Include(k => k.Items).Where(k => k.OrderId == id).OrderBy(k => k.Id).ToListAsync(cancellationToken);
        var stations = await _db.PreparationStations.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Code, cancellationToken);
        var bill = await _db.Bills.AsNoTracking()
            .Where(b => b.OrderId == id && b.Status != BillStatus.Voided)
            .Select(b => new OrderBillDto
            {
                Id = b.Id,
                BillNumber = b.BillNumber,
                InvoiceNumber = b.InvoiceNumber,
                Status = b.Status,
                PaymentStatus = b.PaymentStatus,
                GrandTotal = b.GrandTotal,
                PaidAmount = b.PaidAmount,
            })
            .FirstOrDefaultAsync(cancellationToken);
        return ToDetail(order, waiterName, await CanModifyAsync(order, cancellationToken), tickets, stations) with { Bill = bill };
    }

    public async Task<OrderSummaryDto?> SummaryAsync(int orderId, CancellationToken cancellationToken) =>
        await Summaries(_db.Orders.AsNoTracking().Where(o => o.Id == orderId)).FirstOrDefaultAsync(cancellationToken);

    public IQueryable<OrderSummaryDto> Summaries(IQueryable<Order> orders) =>
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

    public Task<string> WaiterNameAsync(int waiterId, CancellationToken cancellationToken) =>
        _db.Users.Where(u => u.Id == waiterId).Select(u => u.DisplayName).FirstOrDefaultAsync(cancellationToken)!;

    private async Task<bool> CanModifyAsync(Order order, CancellationToken cancellationToken) =>
        MayEditAny || order.WaiterId == _currentUser.UserId || await AnyWaiterMayEditAsync(cancellationToken);

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

    private static OrderDetailDto ToDetail(Order order, string waiterName, bool canModify, IReadOnlyList<KitchenOrder> tickets, IReadOnlyDictionary<int, string> stations)
    {
        var ticketOf = tickets.SelectMany(t => t.Items.Select(i => (i.OrderItemId, Ticket: t))).ToDictionary(x => x.OrderItemId, x => x.Ticket);
        return new OrderDetailDto
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
                    TicketId = ticketOf.GetValueOrDefault(i.Id)?.Id,
                    TicketStatus = ticketOf.GetValueOrDefault(i.Id)?.Status,
                })
                .ToList(),
            Tickets = tickets.Select(t => new OrderTicketDto
            {
                Id = t.Id,
                TicketNumber = t.TicketNumber,
                BatchNumber = t.BatchNumber,
                StationCode = stations.GetValueOrDefault(t.PreparationStationId, string.Empty),
                Status = t.Status,
            }).ToList(),
            ApproxSubtotal = order.ApproxSubtotal,
            CanModify = canModify && OrderStateMachine.IsActive(order.Status),
            RowVersion = RowVersions.Encode(order.RowVersion),
        };
    }
}
