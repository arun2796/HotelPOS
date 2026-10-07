using System.Globalization;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Floor;
using HotelPOS.Application.Kitchen;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Billing;
using HotelPOS.Domain.Common;
using HotelPOS.Domain.Floor;
using HotelPOS.Domain.Kitchen;
using HotelPOS.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Billing;

public interface IBillingService
{
    Task<Result<BillDetailDto>> RequestBillAsync(int orderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BillSummaryDto>> ListPendingAsync(CancellationToken cancellationToken = default);

    Task<Result<BillDetailDto>> GetAsync(int id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BillSummaryDto>> ListClosedAsync(ClosedBillQuery query, CancellationToken cancellationToken = default);

    Task<Result<BillDetailDto>> ClaimAsync(int id, ClaimBillRequest request, CancellationToken cancellationToken = default);

    Task<Result<BillDetailDto>> ReleaseAsync(int id, CancellationToken cancellationToken = default);

    Task<Result<BillDetailDto>> SetDiscountAsync(int id, ApplyDiscountRequest request, CancellationToken cancellationToken = default);

    Task<Result<BillDetailDto>> ClearDiscountAsync(int id, BillActionRequest request, CancellationToken cancellationToken = default);

    Task<Result<BillDetailDto>> SetCustomerAsync(int id, UpdateCustomerRequest request, CancellationToken cancellationToken = default);

    Task<Result<BillDetailDto>> FinalizeAsync(int id, BillActionRequest request, CancellationToken cancellationToken = default);

    Task<Result<BillDetailDto>> AddPaymentAsync(int id, AddPaymentRequest request, Guid idempotencyKey, CancellationToken cancellationToken = default);

    Task<Result<BillDetailDto>> CloseAsync(int id, CancellationToken cancellationToken = default);

    Task<Result<BillDetailDto>> ReopenAsync(int id, ReopenBillRequest request, CancellationToken cancellationToken = default);

    Task<Result<BillDetailDto>> VoidAsync(int id, VoidBillRequest request, CancellationToken cancellationToken = default);

    Task<Result<BillDetailDto>> RefundAsync(int id, RefundRequest request, Guid idempotencyKey, CancellationToken cancellationToken = default);

    Task<int> ReleaseExpiredClaimsAsync(CancellationToken cancellationToken = default);
}

public sealed class BillingService : IBillingService
{
    private static readonly RealtimeAudience CashierDesk = RealtimeAudience.ForRoles(Roles.Cashier, Roles.Manager, Roles.Admin);
    private static readonly RealtimeAudience OrderWatchers = RealtimeAudience.ForRoles(Roles.Kitchen, Roles.Cashier, Roles.Manager, Roles.Admin);

    private readonly IAppDbContext _db;
    private readonly IAuditService _audit;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IRealtimeNotifier _realtime;
    private readonly TableEvents _tableEvents;
    private readonly KitchenSync _kitchen;
    private readonly IInvoiceNumberService _invoices;
    private readonly IManagerApprovalService _approvals;

    public BillingService(
        IAppDbContext db,
        IAuditService audit,
        ICurrentUser currentUser,
        IClock clock,
        IRealtimeNotifier realtime,
        TableEvents tableEvents,
        KitchenSync kitchen,
        IInvoiceNumberService invoices,
        IManagerApprovalService approvals)
    {
        _db = db;
        _audit = audit;
        _currentUser = currentUser;
        _clock = clock;
        _realtime = realtime;
        _tableEvents = tableEvents;
        _kitchen = kitchen;
        _invoices = invoices;
        _approvals = approvals;
    }

    private delegate Task<AppError?> BillStep(BillScope scope, List<Func<Task>> afterCommit);

    private int UserId => _currentUser.UserId ?? throw new InvalidOperationException("Billing requires a signed-in user.");

    private bool IsManager => ManagerApprovalService.IsManager(_currentUser);

    private bool IsWaiterOnly => _currentUser.IsInRole(Roles.Waiter) && !IsManager && !_currentUser.IsInRole(Roles.Cashier);

    public async Task<Result<BillDetailDto>> RequestBillAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _db.Orders.Include(o => o.Table).Include(o => o.Items).ThenInclude(i => i.Modifiers)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
        if (order is null)
        {
            return AppErrors.NotFound("Order", orderId);
        }

        if (IsWaiterOnly && order.WaiterId != UserId && !await _db.BoolSettingAsync(SettingKeys.AllowAnyWaiterToEditOrders, false, cancellationToken))
        {
            return AppErrors.Forbidden($"Order {order.OrderNumber} belongs to another waiter. Ask them or a manager to request the bill.");
        }

        var existing = await _db.Bills.AsNoTracking()
            .Where(b => b.OrderId == orderId && b.Status != BillStatus.Voided)
            .Select(b => (int?)b.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is { } existingId)
        {
            return AppErrors.InvalidState($"The bill for order {order.OrderNumber} was already requested.", await DetailAsync(existingId, cancellationToken));
        }

        var table = order.Table!;
        var now = _clock.UtcNow;
        Bill bill;
        try
        {
            order.RequestBill(now, await _db.BoolSettingAsync(SettingKeys.AllowBillBeforeReady, false, cancellationToken));
            var lines = order.ActiveItems
                .OrderBy(i => i.BatchNumber).ThenBy(i => i.Id)
                .Select(i => new BillLineInput(i.Id, i.ItemName, i.Quantity, i.UnitPrice, i.Modifiers.Sum(m => m.PriceDelta), i.TaxRatePercent))
                .ToList();
            bill = new Bill(order, table.Code, lines, await RoundOffAsync(cancellationToken));
        }
        catch (DomainException ex)
        {
            return new AppError(ex.Code, ex.Message);
        }

        if (table.CurrentOrderId == order.Id)
        {
            table.FollowOrder(order.Status);
        }

        _db.Bills.Add(bill);
        try
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            _audit.Record(AuditActions.BillRequested, nameof(Bill), bill.Id.ToString(CultureInfo.InvariantCulture),
                newValues: new { bill.BillNumber, order.OrderNumber, Table = table.Code, bill.GrandTotal });
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return AppErrors.Concurrency("order");
        }

        await _realtime.PublishAsync(HubEvents.BillRequested, new BillRequestedEvent
        {
            OccurredAtUtc = _clock.UtcNow,
            EntityId = bill.Id,
            EntityVersion = RowVersions.Encode(bill.RowVersion),
            BillId = bill.Id,
            BillNumber = bill.BillNumber,
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            TableCode = table.Code,
            WaiterName = await UserNameAsync(order.WaiterId, CancellationToken.None) ?? string.Empty,
            GrandTotal = bill.GrandTotal,
        }, CashierDesk, CancellationToken.None);
        await _tableEvents.PublishStatusAsync(table, order.OrderNumber);
        await PublishOrderUpdatedAsync(order, OrderChangeTypes.BillRequested);
        return await GetAsync(bill.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<BillSummaryDto>> ListPendingAsync(CancellationToken cancellationToken = default) =>
        await SummariesAsync(
            _db.Bills.AsNoTracking()
                .Where(b => b.Status == BillStatus.Open || b.Status == BillStatus.Finalized)
                .OrderBy(b => b.CreatedAt),
            cancellationToken);

    public async Task<Result<BillDetailDto>> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var detail = await DetailAsync(id, cancellationToken);
        if (detail is null)
        {
            return AppErrors.NotFound("Bill", id);
        }

        if (IsWaiterOnly && detail.WaiterId != UserId && !await _db.BoolSettingAsync(SettingKeys.AllowAnyWaiterToEditOrders, false, cancellationToken))
        {
            return AppErrors.Forbidden("This bill belongs to another waiter's order.");
        }

        return detail;
    }

    public async Task<IReadOnlyList<BillSummaryDto>> ListClosedAsync(ClosedBillQuery query, CancellationToken cancellationToken = default)
    {
        var (from, to) = await _db.BusinessDayAsync(_clock, query.Date, cancellationToken);
        var bills = _db.Bills.AsNoTracking().Where(b =>
            (b.Status == BillStatus.Settled && b.SettledAt >= from && b.SettledAt < to)
            || (b.Status == BillStatus.Voided && b.VoidedAt >= from && b.VoidedAt < to));

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToUpperInvariant();
            var number = int.TryParse(search, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : -1;
            bills = bills.Where(b =>
                b.TableCode == search
                || (b.InvoiceNumber != null && b.InvoiceNumber.ToUpper().Contains(search))
                || b.BillNumber == number
                || _db.Orders.Any(o => o.Id == b.OrderId && o.OrderNumber == number));
        }

        return await SummariesAsync(bills.OrderByDescending(b => b.SettledAt ?? b.VoidedAt), cancellationToken);
    }

    public async Task<Result<BillDetailDto>> ClaimAsync(int id, ClaimBillRequest request, CancellationToken cancellationToken = default)
    {
        var bill = await _db.Bills.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (bill is null)
        {
            return AppErrors.NotFound("Bill", id);
        }

        if (!bill.IsPending)
        {
            return AppErrors.InvalidState($"Bill {bill.BillNumber} is {bill.Status} and needs no handling.", await DetailAsync(id, cancellationToken));
        }

        var now = _clock.UtcNow;
        var heldByOther = bill.IsHeldByOther(UserId, _currentUser.DeviceId, now);
        if (heldByOther && !(request.Override && IsManager))
        {
            return AppErrors.BillClaimed(await HolderAsync(bill, cancellationToken), await DetailAsync(id, cancellationToken));
        }

        var wasMine = bill.IsHeldBy(UserId, _currentUser.DeviceId, now);
        var previousHolder = heldByOther ? await HolderAsync(bill, cancellationToken) : null;
        bill.Claim(UserId, _currentUser.DeviceId, now);
        if (heldByOther)
        {
            _audit.Record(AuditActions.BillClaimOverridden, nameof(Bill), bill.Id.ToString(CultureInfo.InvariantCulture),
                new { Holder = previousHolder }, new { Holder = _currentUser.DeviceName ?? _currentUser.UserName });
        }
        else if (!wasMine)
        {
            _audit.Record(AuditActions.BillClaimed, nameof(Bill), bill.Id.ToString(CultureInfo.InvariantCulture),
                newValues: new { bill.BillNumber, Holder = _currentUser.DeviceName ?? _currentUser.UserName });
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Two counters opened the bill at the same moment; the other one won.
            var current = await _db.Bills.AsNoTracking().FirstAsync(b => b.Id == id, cancellationToken);
            return current.IsHeldByOther(UserId, _currentUser.DeviceId, now)
                ? AppErrors.BillClaimed(await HolderAsync(current, cancellationToken), await DetailAsync(id, cancellationToken))
                : AppErrors.Concurrency("bill", await DetailAsync(id, cancellationToken));
        }

        if (!wasMine)
        {
            await PublishBillUpdatedAsync(bill, BillChangeTypes.Claimed);
        }

        return await GetAsync(id, cancellationToken);
    }

    public async Task<Result<BillDetailDto>> ReleaseAsync(int id, CancellationToken cancellationToken = default)
    {
        var bill = await _db.Bills.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (bill is null)
        {
            return AppErrors.NotFound("Bill", id);
        }

        var now = _clock.UtcNow;
        if (bill.ClaimExpiresAt is null || bill.ClaimExpiresAt <= now)
        {
            return await GetAsync(id, cancellationToken);
        }

        if (bill.IsHeldByOther(UserId, _currentUser.DeviceId, now) && !IsManager)
        {
            return AppErrors.BillClaimed(await HolderAsync(bill, cancellationToken), await DetailAsync(id, cancellationToken));
        }

        bill.Release();
        _audit.Record(AuditActions.BillReleased, nameof(Bill), bill.Id.ToString(CultureInfo.InvariantCulture), newValues: new { bill.BillNumber });
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return AppErrors.Concurrency("bill", await DetailAsync(id, cancellationToken));
        }

        await PublishBillUpdatedAsync(bill, BillChangeTypes.Released);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<Result<BillDetailDto>> SetDiscountAsync(int id, ApplyDiscountRequest request, CancellationToken cancellationToken = default)
    {
        int? approvedBy = null;
        Discount? predefined = null;
        DiscountType type = default;
        decimal value = 0;

        return await RunAsync(id, request.RowVersion, claim: true,
            prepare: async scope =>
            {
                bool needsApproval;
                if (request.DiscountId is { } discountId)
                {
                    predefined = await _db.Discounts.AsNoTracking().FirstOrDefaultAsync(d => d.Id == discountId, cancellationToken);
                    if (predefined is not { IsActive: true })
                    {
                        return AppErrors.Validation("discountId", "That discount is not available.");
                    }

                    (type, value, needsApproval) = (predefined.Type, predefined.Value, predefined.RequiresApproval);
                }
                else
                {
                    (type, value) = (request.Type!.Value, request.Value!.Value);
                    var limit = await _db.DecimalSettingAsync(SettingKeys.MaxCashierDiscountPercent, 10m, cancellationToken);
                    needsApproval = BillCalculator.DiscountPercentOf(new BillDiscountInput(type, value), scope.Bill.Subtotal) > limit;
                }

                if (needsApproval)
                {
                    var approval = await _approvals.ApproveAsync(request.Approval, "this discount", cancellationToken);
                    if (approval.IsFailure)
                    {
                        return approval.Error;
                    }

                    approvedBy = approval.Value;
                }

                return null;
            },
            step: async (scope, after) =>
            {
                var bill = scope.Bill;
                var before = new { bill.DiscountType, bill.DiscountValue, bill.DiscountAmount, bill.GrandTotal };
                bill.ApplyDiscount(predefined?.Id, type, value, request.Reason ?? predefined?.Name, approvedBy, await RoundOffAsync(cancellationToken));
                _audit.Record(AuditActions.BillDiscountApplied, nameof(Bill), bill.Id.ToString(CultureInfo.InvariantCulture), before,
                    new { bill.DiscountType, bill.DiscountValue, bill.DiscountAmount, bill.GrandTotal, bill.DiscountReason, ApprovedBy = approvedBy });
                after.Add(() => PublishBillUpdatedAsync(bill, BillChangeTypes.DiscountChanged));
                return null;
            },
            cancellationToken);
    }

    public Task<Result<BillDetailDto>> ClearDiscountAsync(int id, BillActionRequest request, CancellationToken cancellationToken = default) =>
        RunAsync(id, request.RowVersion, claim: true, prepare: null,
            step: async (scope, after) =>
            {
                var bill = scope.Bill;
                var before = new { bill.DiscountType, bill.DiscountValue, bill.DiscountAmount, bill.GrandTotal };
                bill.ClearDiscount(await RoundOffAsync(cancellationToken));
                _audit.Record(AuditActions.BillDiscountCleared, nameof(Bill), bill.Id.ToString(CultureInfo.InvariantCulture), before, new { bill.GrandTotal });
                after.Add(() => PublishBillUpdatedAsync(bill, BillChangeTypes.DiscountChanged));
                return null;
            },
            cancellationToken);

    public Task<Result<BillDetailDto>> SetCustomerAsync(int id, UpdateCustomerRequest request, CancellationToken cancellationToken = default) =>
        RunAsync(id, request.RowVersion, claim: true, prepare: null,
            step: (scope, after) =>
            {
                var bill = scope.Bill;
                var before = new { bill.CustomerName, bill.CustomerPhone, bill.CustomerGstin };
                bill.SetCustomer(request.Name, request.Phone, request.Gstin);
                _audit.Record(AuditActions.BillCustomerUpdated, nameof(Bill), bill.Id.ToString(CultureInfo.InvariantCulture), before,
                    new { bill.CustomerName, bill.CustomerPhone, bill.CustomerGstin });
                after.Add(() => PublishBillUpdatedAsync(bill, BillChangeTypes.CustomerChanged));
                return Task.FromResult<AppError?>(null);
            },
            cancellationToken);

    public Task<Result<BillDetailDto>> FinalizeAsync(int id, BillActionRequest request, CancellationToken cancellationToken = default) =>
        RunAsync(id, request.RowVersion, claim: true, prepare: null,
            step: async (scope, after) =>
            {
                await FinalizeCoreAsync(scope, after, cancellationToken);
                return null;
            },
            cancellationToken);

    public async Task<Result<BillDetailDto>> AddPaymentAsync(int id, AddPaymentRequest request, Guid idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (await _db.Payments.AnyAsync(p => p.IdempotencyKey == idempotencyKey, cancellationToken))
        {
            return await GetAsync(id, cancellationToken);
        }

        PaymentMethod? method = null;
        return await RunAsync(id, request.RowVersion, claim: true,
            prepare: async _ =>
            {
                method = await _db.PaymentMethods.AsNoTracking().FirstOrDefaultAsync(m => m.Id == request.PaymentMethodId, cancellationToken);
                return method is null ? AppErrors.Validation("paymentMethodId", "Choose a payment method.") : null;
            },
            step: async (scope, after) =>
            {
                var bill = scope.Bill;
                if (bill.Status == BillStatus.Open)
                {
                    await FinalizeCoreAsync(scope, after, cancellationToken);
                }

                var payment = bill.AddPayment(method!, request.Amount, request.Tendered, request.Reference,
                    UserId, _currentUser.DeviceId, idempotencyKey, _clock.UtcNow);
                _audit.Record(AuditActions.PaymentReceived, nameof(Bill), bill.Id.ToString(CultureInfo.InvariantCulture), newValues: new
                {
                    bill.InvoiceNumber,
                    Method = method!.Code,
                    payment.Amount,
                    payment.TenderedAmount,
                    payment.ChangeAmount,
                    Reference = Mask(payment.Reference),
                    BalanceDue = bill.BalanceDue,
                });

                if (bill.Status == BillStatus.Settled)
                {
                    await SettleCoreAsync(scope, after, cancellationToken);
                }
                else
                {
                    after.Add(() => PublishBillUpdatedAsync(bill, BillChangeTypes.PaymentReceived));
                }

                return null;
            },
            cancellationToken);
    }

    public async Task<Result<BillDetailDto>> CloseAsync(int id, CancellationToken cancellationToken = default)
    {
        var scope = await LoadAsync(id, cancellationToken);
        if (scope is null)
        {
            return AppErrors.NotFound("Bill", id);
        }

        if (scope.Bill.Status != BillStatus.Settled || scope.Order.Status != OrderStatus.Paid)
        {
            return AppErrors.InvalidState($"Order {scope.Order.OrderNumber} is {scope.Order.Status} and cannot be closed.", await DetailAsync(id, cancellationToken));
        }

        scope.Order.Close(_clock.UtcNow);
        _audit.Record(AuditActions.BillClosed, nameof(Bill), scope.Bill.Id.ToString(CultureInfo.InvariantCulture), newValues: new { scope.Order.OrderNumber });
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return AppErrors.Concurrency("order", await DetailAsync(id, cancellationToken));
        }

        await PublishOrderUpdatedAsync(scope.Order, OrderChangeTypes.Completed);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<Result<BillDetailDto>> ReopenAsync(int id, ReopenBillRequest request, CancellationToken cancellationToken = default)
    {
        int? approvedBy = null;
        return await RunAsync(id, request.RowVersion, claim: true,
            prepare: async scope =>
            {
                if (scope.Bill.Status != BillStatus.Finalized)
                {
                    return null;
                }

                // A finalized bill has an invoice number; reopening cancels that invoice.
                var approval = await _approvals.ApproveAsync(request.Approval, "reopening an invoiced bill", cancellationToken);
                approvedBy = approval.IsSuccess ? approval.Value : null;
                return approval.Error;
            },
            step: async (scope, after) =>
            {
                var (bill, order, table) = (scope.Bill, scope.Order, scope.Table);
                var tableBefore = table.Status;
                bill.Reopen(request.Reason ?? "Reopened for changes", _clock.UtcNow);
                order.ReopenFromBill();
                var tickets = await _kitchen.LoadTicketsAsync(order.Id, cancellationToken);
                _kitchen.Recompute(order, tickets, table);
                _audit.Record(AuditActions.BillReopened, nameof(Bill), bill.Id.ToString(CultureInfo.InvariantCulture), newValues: new
                {
                    bill.BillNumber,
                    CancelledInvoice = bill.InvoiceNumber,
                    order.OrderNumber,
                    Reason = bill.VoidReason,
                    ApprovedBy = approvedBy,
                });
                after.Add(() => PublishBillUpdatedAsync(bill, BillChangeTypes.Reopened));
                after.Add(() => PublishOrderUpdatedAsync(order, OrderChangeTypes.BillReopened));
                if (table.Status != tableBefore)
                {
                    after.Add(() => _tableEvents.PublishStatusAsync(table, order.OrderNumber));
                }

                return null;
            },
            cancellationToken);
    }

    public async Task<Result<BillDetailDto>> VoidAsync(int id, VoidBillRequest request, CancellationToken cancellationToken = default)
    {
        int approvedBy = 0;
        return await RunAsync(id, request.RowVersion, claim: true,
            prepare: async _ =>
            {
                var approval = await _approvals.ApproveAsync(request.Approval, "voiding a bill", cancellationToken);
                approvedBy = approval.IsSuccess ? approval.Value : 0;
                return approval.Error;
            },
            step: async (scope, after) =>
            {
                var (bill, order, table) = (scope.Bill, scope.Order, scope.Table);
                var now = _clock.UtcNow;
                bill.Void(request.Reason, now);
                var openTickets = (await _kitchen.LoadTicketsAsync(order.Id, cancellationToken)).Where(t => t.IsOpen).ToList();
                foreach (var ticket in openTickets)
                {
                    ticket.Cancel(now);
                }

                order.Cancel(UserId, request.Reason, now);
                if (table.CurrentOrderId == order.Id)
                {
                    table.DetachOrder(keepGuestsSeated: false);
                }

                _audit.Record(AuditActions.BillVoided, nameof(Bill), bill.Id.ToString(CultureInfo.InvariantCulture), newValues: new
                {
                    bill.BillNumber,
                    bill.InvoiceNumber,
                    bill.GrandTotal,
                    order.OrderNumber,
                    Reason = bill.VoidReason,
                    ApprovedBy = approvedBy,
                });
                after.Add(() => PublishBillUpdatedAsync(bill, BillChangeTypes.Voided));
                after.Add(() => _realtime.PublishAsync(HubEvents.OrderCancelled, new OrderCancelledEvent
                {
                    OccurredAtUtc = _clock.UtcNow,
                    EntityId = order.Id,
                    EntityVersion = RowVersions.Encode(order.RowVersion),
                    OrderId = order.Id,
                    OrderNumber = order.OrderNumber,
                    TableCode = table.Code,
                    Reason = order.CancelReason,
                }, OrderWatchers with { UserIds = new[] { order.WaiterId } }, CancellationToken.None));
                after.Add(() => _tableEvents.PublishStatusAsync(table));
                after.Add(() => PublishTicketsAsync(order, table, openTickets));
                return null;
            },
            cancellationToken);
    }

    public async Task<Result<BillDetailDto>> RefundAsync(int id, RefundRequest request, Guid idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (await _db.Payments.AnyAsync(p => p.IdempotencyKey == idempotencyKey, cancellationToken))
        {
            return await GetAsync(id, cancellationToken);
        }

        int approvedBy = 0;
        return await RunAsync(id, request.RowVersion, claim: false,
            prepare: async _ =>
            {
                var approval = await _approvals.ApproveAsync(request.Approval, "a refund", cancellationToken);
                approvedBy = approval.IsSuccess ? approval.Value : 0;
                return approval.Error;
            },
            step: (scope, after) =>
            {
                var bill = scope.Bill;
                var refund = bill.Refund(request.PaymentId, request.Amount, request.Reason, UserId, _currentUser.DeviceId, idempotencyKey, _clock.UtcNow);
                _audit.Record(AuditActions.PaymentRefunded, nameof(Bill), bill.Id.ToString(CultureInfo.InvariantCulture), newValues: new
                {
                    bill.InvoiceNumber,
                    PaymentId = request.PaymentId,
                    Amount = -refund.Amount,
                    refund.RefundReason,
                    bill.RefundedAmount,
                    ApprovedBy = approvedBy,
                });
                after.Add(() => PublishBillUpdatedAsync(bill, BillChangeTypes.Refunded));
                return Task.FromResult<AppError?>(null);
            },
            cancellationToken);
    }

    public async Task<int> ReleaseExpiredClaimsAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var expired = await _db.Bills.Where(b => b.ClaimExpiresAt != null && b.ClaimExpiresAt <= now).ToListAsync(cancellationToken);
        foreach (var bill in expired)
        {
            bill.Release();
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A cashier touched one of them meanwhile; the next run picks up whatever is still expired.
            return 0;
        }

        foreach (var bill in expired)
        {
            await PublishBillUpdatedAsync(bill, BillChangeTypes.Released);
        }

        return expired.Count;
    }

    private async Task<Result<BillDetailDto>> RunAsync(
        int id,
        string? rowVersion,
        bool claim,
        Func<BillScope, Task<AppError?>>? prepare,
        BillStep step,
        CancellationToken cancellationToken)
    {
        var scope = await LoadAsync(id, cancellationToken);
        if (scope is null)
        {
            return AppErrors.NotFound("Bill", id);
        }

        var bill = scope.Bill;
        var now = _clock.UtcNow;
        if (claim && bill.IsPending && bill.IsHeldByOther(UserId, _currentUser.DeviceId, now))
        {
            return AppErrors.BillClaimed(await HolderAsync(bill, cancellationToken), await DetailAsync(id, cancellationToken));
        }

        if (rowVersion is not null && !RowVersions.Matches(bill.RowVersion, rowVersion))
        {
            return AppErrors.Concurrency("bill", await DetailAsync(id, cancellationToken));
        }

        // Approvals run before the transaction so a rejected attempt stays on record.
        if (prepare is not null && await prepare(scope) is { } rejected)
        {
            return rejected;
        }

        var afterCommit = new List<Func<Task>>();
        await using (var transaction = await _db.Database.BeginTransactionAsync(cancellationToken))
        {
            try
            {
                if (claim && bill.IsPending)
                {
                    bill.Claim(UserId, _currentUser.DeviceId, now);
                }

                if (await step(scope, afterCommit) is { } error)
                {
                    return error;
                }

                await _db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DomainException ex)
            {
                return new AppError(ex.Code, ex.Message)
                {
                    Data = ex.Code == ErrorCodes.InvalidStateTransition ? await DetailAsync(id, cancellationToken) : null,
                };
            }
            catch (DbUpdateConcurrencyException)
            {
                return AppErrors.Concurrency("bill", await DetailAsync(id, cancellationToken));
            }
        }

        foreach (var publish in afterCommit)
        {
            await publish();
        }

        return await GetAsync(id, cancellationToken);
    }

    private async Task FinalizeCoreAsync(BillScope scope, List<Func<Task>> after, CancellationToken cancellationToken)
    {
        var (bill, order) = (scope.Bill, scope.Order);
        if (bill.Status != BillStatus.Open)
        {
            throw new DomainException($"Bill {bill.BillNumber} is already {bill.Status}.", ErrorCodes.InvalidStateTransition);
        }

        var now = _clock.UtcNow;
        bill.Finalize(await _invoices.NextAsync(now, cancellationToken), UserId, now);
        order.MarkBilled();
        _audit.Record(AuditActions.BillFinalized, nameof(Bill), bill.Id.ToString(CultureInfo.InvariantCulture),
            newValues: new { bill.BillNumber, bill.InvoiceNumber, bill.GrandTotal, order.OrderNumber });
        after.Add(() => PublishOrderUpdatedAsync(order, OrderChangeTypes.Billed));
        if (bill.Status == BillStatus.Settled)
        {
            await SettleCoreAsync(scope, after, cancellationToken);
        }
        else
        {
            after.Add(() => PublishBillUpdatedAsync(bill, BillChangeTypes.Finalized));
        }
    }

    // Bill, order, table and tickets change in the caller's transaction; events go out after the commit.
    private async Task SettleCoreAsync(BillScope scope, List<Func<Task>> after, CancellationToken cancellationToken)
    {
        var (bill, order, table) = (scope.Bill, scope.Order, scope.Table);
        var now = _clock.UtcNow;
        order.MarkPaid();
        if (await _db.BoolSettingAsync(SettingKeys.AutoCloseOnFullPayment, true, cancellationToken))
        {
            order.Close(now);
        }

        var closed = (await _kitchen.LoadTicketsAsync(order.Id, cancellationToken)).Where(t => t.IsOpen).ToList();
        foreach (var ticket in closed)
        {
            ticket.CloseOnSettle(UserId, now);
        }

        if (table.CurrentOrderId == order.Id)
        {
            table.DetachOrder(keepGuestsSeated: false);
        }

        _audit.Record(AuditActions.BillSettled, nameof(Bill), bill.Id.ToString(CultureInfo.InvariantCulture), newValues: new
        {
            bill.InvoiceNumber,
            bill.GrandTotal,
            bill.PaidAmount,
            Payments = bill.Payments.Count,
            order.OrderNumber,
            OrderStatus = order.Status,
        });

        after.Add(() => _realtime.PublishAsync(HubEvents.PaymentCompleted, new PaymentCompletedEvent
        {
            OccurredAtUtc = _clock.UtcNow,
            EntityId = bill.Id,
            EntityVersion = RowVersions.Encode(bill.RowVersion),
            BillId = bill.Id,
            OrderId = order.Id,
            TableId = table.Id,
            TableCode = table.Code,
            InvoiceNumber = bill.InvoiceNumber,
            GrandTotal = bill.GrandTotal,
        }, RealtimeAudience.ForRoles(Roles.Cashier, Roles.Waiter, Roles.Manager, Roles.Admin), CancellationToken.None));
        after.Add(() => _tableEvents.PublishStatusAsync(table));
        after.Add(() => PublishOrderUpdatedAsync(order, order.Status == OrderStatus.Completed ? OrderChangeTypes.Completed : OrderChangeTypes.Paid));
        after.Add(() => PublishTicketsAsync(order, table, closed));
    }

    private async Task<BillScope?> LoadAsync(int id, CancellationToken cancellationToken)
    {
        var bill = await _db.Bills.Include(b => b.Items).Include(b => b.Payments).FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (bill is null)
        {
            return null;
        }

        var order = await _db.Orders.Include(o => o.Table).Include(o => o.Items).ThenInclude(i => i.Modifiers)
            .FirstAsync(o => o.Id == bill.OrderId, cancellationToken);
        return new BillScope(bill, order, order.Table!);
    }

    private Task<bool> RoundOffAsync(CancellationToken cancellationToken) =>
        _db.BoolSettingAsync(SettingKeys.RoundOffTotals, true, cancellationToken);

    private async Task<string> HolderAsync(Bill bill, CancellationToken cancellationToken)
    {
        if (bill.ClaimedByDeviceId is { } deviceId
            && await _db.Devices.AsNoTracking().Where(d => d.Id == deviceId).Select(d => d.Name).FirstOrDefaultAsync(cancellationToken) is { } device)
        {
            return device;
        }

        return bill.ClaimedByUserId is { } userId ? await UserNameAsync(userId, cancellationToken) ?? "another cashier" : "another cashier";
    }

    private Task<string?> UserNameAsync(int userId, CancellationToken cancellationToken) =>
        _db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => (string?)u.DisplayName).FirstOrDefaultAsync(cancellationToken);

    private async Task<BillDetailDto?> DetailAsync(int id, CancellationToken cancellationToken)
    {
        var bill = await _db.Bills.AsNoTracking().Include(b => b.Items).Include(b => b.Payments).FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (bill is null)
        {
            return null;
        }

        var order = await _db.Orders.AsNoTracking().Where(o => o.Id == bill.OrderId)
            .Select(o => new { o.OrderNumber, o.TableId, o.GuestCount })
            .FirstAsync(cancellationToken);
        var userIds = new[] { bill.WaiterId, bill.SettledBy, bill.DiscountApprovedBy, bill.ClaimedByUserId }
            .Where(u => u is not null).Select(u => u!.Value)
            .Concat(bill.Payments.Select(p => p.ReceivedBy))
            .Distinct().ToList();
        var users = await _db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);
        var methods = await _db.PaymentMethods.AsNoTracking().ToDictionaryAsync(m => m.Id, cancellationToken);
        var discountName = bill.DiscountId is { } discountId
            ? await _db.Discounts.AsNoTracking().Where(d => d.Id == discountId).Select(d => d.Name).FirstOrDefaultAsync(cancellationToken)
            : null;
        var now = _clock.UtcNow;
        var claimed = bill.ClaimExpiresAt > now;
        var device = claimed && bill.ClaimedByDeviceId is { } deviceId
            ? await _db.Devices.AsNoTracking().Where(d => d.Id == deviceId).Select(d => d.Name).FirstOrDefaultAsync(cancellationToken)
            : null;
        var split = await _db.SettingAsync(SettingKeys.TaxSplitDisplay, cancellationToken);
        string? Name(int? userId) => userId is { } u ? users.GetValueOrDefault(u) : null;

        return new BillDetailDto
        {
            Id = bill.Id,
            BillNumber = bill.BillNumber,
            InvoiceNumber = bill.InvoiceNumber,
            OrderId = bill.OrderId,
            OrderNumber = order.OrderNumber,
            TableId = order.TableId,
            TableCode = bill.TableCode,
            WaiterId = bill.WaiterId,
            WaiterName = Name(bill.WaiterId) ?? string.Empty,
            GuestCount = order.GuestCount,
            Status = bill.Status,
            PaymentStatus = bill.PaymentStatus,
            Items = bill.Items.OrderBy(i => i.Id).Select(i => new BillItemDto
            {
                Id = i.Id,
                OrderItemId = i.OrderItemId,
                ItemName = i.ItemName,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice,
                ModifiersAmount = i.ModifiersAmount,
                LineSubtotal = i.LineSubtotal,
                DiscountShare = i.DiscountShare,
                TaxRatePercent = i.TaxRatePercent,
                TaxAmount = i.TaxAmount,
                LineTotal = i.LineTotal,
            }).ToList(),
            Subtotal = bill.Subtotal,
            DiscountId = bill.DiscountId,
            DiscountName = discountName,
            DiscountType = bill.DiscountType,
            DiscountValue = bill.DiscountValue,
            DiscountAmount = bill.DiscountAmount,
            DiscountReason = bill.DiscountReason,
            DiscountApprovedBy = Name(bill.DiscountApprovedBy),
            TaxableAmount = bill.TaxableAmount,
            TaxAmount = bill.TaxAmount,
            TaxBreakup = TaxBreakup(bill.Items, split),
            RoundOff = bill.RoundOff,
            GrandTotal = bill.GrandTotal,
            PaidAmount = bill.PaidAmount,
            RefundedAmount = bill.RefundedAmount,
            BalanceDue = bill.BalanceDue,
            Payments = bill.Payments.OrderBy(p => p.PaidAt).ThenBy(p => p.Id).Select(p => new PaymentDto
            {
                Id = p.Id,
                PaymentMethodId = p.PaymentMethodId,
                MethodName = methods.GetValueOrDefault(p.PaymentMethodId)?.Name ?? string.Empty,
                MethodCode = methods.GetValueOrDefault(p.PaymentMethodId)?.Code ?? string.Empty,
                Amount = p.Amount,
                TenderedAmount = p.TenderedAmount,
                ChangeAmount = p.ChangeAmount,
                Reference = p.Reference,
                Status = p.Status,
                ReceivedBy = Name(p.ReceivedBy) ?? string.Empty,
                PaidAtUtc = p.PaidAt,
                RefundOfPaymentId = p.RefundOfPaymentId,
                RefundReason = p.RefundReason,
                RefundableAmount = bill.Status == BillStatus.Settled ? bill.RefundableAmount(p) : 0m,
            }).ToList(),
            CustomerName = bill.CustomerName,
            CustomerPhone = bill.CustomerPhone,
            CustomerGstin = bill.CustomerGstin,
            ClaimedByDevice = device,
            ClaimedByUser = claimed ? Name(bill.ClaimedByUserId) : null,
            ClaimExpiresAtUtc = claimed ? bill.ClaimExpiresAt : null,
            IsClaimedByMe = _currentUser.UserId is { } me && bill.IsHeldBy(me, _currentUser.DeviceId, now),
            RequestedAtUtc = bill.CreatedAt,
            FinalizedAtUtc = bill.FinalizedAt,
            SettledAtUtc = bill.SettledAt,
            SettledBy = Name(bill.SettledBy),
            VoidedAtUtc = bill.VoidedAt,
            VoidReason = bill.VoidReason,
            ServerTimeUtc = now,
            RowVersion = RowVersions.Encode(bill.RowVersion),
        };
    }

    internal static IReadOnlyList<TaxBreakupDto> TaxBreakup(IEnumerable<BillItem> items, string? splitDisplay)
    {
        var groups = items
            .Where(i => i.TaxRatePercent > 0)
            .GroupBy(i => i.TaxRatePercent)
            .OrderBy(g => g.Key)
            .Select(g => (Rate: g.Key, Taxable: g.Sum(i => i.LineSubtotal - i.DiscountShare), Tax: g.Sum(i => i.TaxAmount)));
        var split = string.Equals(splitDisplay, "CGST_SGST", StringComparison.OrdinalIgnoreCase);
        var rows = new List<TaxBreakupDto>();
        foreach (var (rate, taxable, tax) in groups)
        {
            if (!split)
            {
                rows.Add(new TaxBreakupDto { Label = $"GST {rate:0.##}%", RatePercent = rate, TaxableAmount = taxable, TaxAmount = tax });
                continue;
            }

            var half = rate / 2;
            var central = BillCalculator.Money(tax / 2);
            rows.Add(new TaxBreakupDto { Label = $"CGST {half:0.##}%", RatePercent = half, TaxableAmount = taxable, TaxAmount = central });
            rows.Add(new TaxBreakupDto { Label = $"SGST {half:0.##}%", RatePercent = half, TaxableAmount = taxable, TaxAmount = tax - central });
        }

        return rows;
    }

    private async Task<IReadOnlyList<BillSummaryDto>> SummariesAsync(IQueryable<Bill> bills, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var rows = await bills.Select(b => new
        {
            b.Id,
            b.BillNumber,
            b.InvoiceNumber,
            b.OrderId,
            OrderNumber = _db.Orders.Where(o => o.Id == b.OrderId).Select(o => o.OrderNumber).FirstOrDefault(),
            b.TableCode,
            WaiterName = _db.Users.Where(u => u.Id == b.WaiterId).Select(u => u.DisplayName).FirstOrDefault(),
            b.Status,
            b.PaymentStatus,
            b.GrandTotal,
            b.PaidAmount,
            b.RefundedAmount,
            b.CreatedAt,
            b.SettledAt,
            b.VoidedAt,
            b.ClaimExpiresAt,
            Device = _db.Devices.Where(d => d.Id == b.ClaimedByDeviceId).Select(d => d.Name).FirstOrDefault(),
            ClaimUser = _db.Users.Where(u => u.Id == b.ClaimedByUserId).Select(u => u.DisplayName).FirstOrDefault(),
        }).ToListAsync(cancellationToken);

        return rows.Select(r =>
        {
            var claimed = r.ClaimExpiresAt > now;
            return new BillSummaryDto
            {
                Id = r.Id,
                BillNumber = r.BillNumber,
                InvoiceNumber = r.InvoiceNumber,
                OrderId = r.OrderId,
                OrderNumber = r.OrderNumber,
                TableCode = r.TableCode,
                WaiterName = r.WaiterName ?? string.Empty,
                Status = r.Status,
                PaymentStatus = r.PaymentStatus,
                GrandTotal = r.GrandTotal,
                PaidAmount = r.PaidAmount,
                RefundedAmount = r.RefundedAmount,
                RequestedAtUtc = r.CreatedAt,
                SettledAtUtc = r.SettledAt,
                VoidedAtUtc = r.VoidedAt,
                ClaimedByDevice = claimed ? r.Device : null,
                ClaimedByUser = claimed ? r.ClaimUser : null,
                ClaimExpiresAtUtc = claimed ? r.ClaimExpiresAt : null,
            };
        }).ToList();
    }

    private async Task PublishBillUpdatedAsync(Bill bill, string changeType)
    {
        var claimed = bill.ClaimExpiresAt > _clock.UtcNow;
        await _realtime.PublishAsync(HubEvents.BillUpdated, new BillUpdatedEvent
        {
            OccurredAtUtc = _clock.UtcNow,
            EntityId = bill.Id,
            EntityVersion = RowVersions.Encode(bill.RowVersion),
            BillId = bill.Id,
            OrderId = bill.OrderId,
            TableCode = bill.TableCode,
            Status = bill.Status,
            PaymentStatus = bill.PaymentStatus,
            ClaimedByDevice = claimed ? await HolderAsync(bill, CancellationToken.None) : null,
            GrandTotal = bill.GrandTotal,
            PaidAmount = bill.PaidAmount,
            ChangeType = changeType,
        }, CashierDesk with { UserIds = new[] { bill.WaiterId } }, CancellationToken.None);
    }

    private Task PublishOrderUpdatedAsync(Order order, string changeType) =>
        _realtime.PublishAsync(HubEvents.OrderUpdated, new OrderUpdatedEvent
        {
            OccurredAtUtc = _clock.UtcNow,
            EntityId = order.Id,
            EntityVersion = RowVersions.Encode(order.RowVersion),
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            Status = order.Status,
            ChangeType = changeType,
        }, OrderWatchers with { UserIds = new[] { order.WaiterId } }, CancellationToken.None);

    private Task PublishTicketsAsync(Order order, Table table, IReadOnlyCollection<KitchenOrder> tickets) =>
        tickets.Count == 0
            ? Task.CompletedTask
            : _kitchen.PublishAsync(order, table, new KitchenChange(order.Status, order.Status, table.Status, table.Status),
                Array.Empty<KitchenOrder>(), tickets, Array.Empty<string>());

    private static string? Mask(string? reference) =>
        reference is null ? null : reference.Length <= 4 ? new string('*', reference.Length) : new string('*', reference.Length - 4) + reference[^4..];

    private sealed record BillScope(Bill Bill, Order Order, Table Table);
}
