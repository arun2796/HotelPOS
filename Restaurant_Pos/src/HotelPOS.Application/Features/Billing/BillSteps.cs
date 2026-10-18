using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Billing;
using HotelPOS.Domain.Floor;
using HotelPOS.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Billing;

internal sealed record BillScope(Bill Bill, Order Order, Table? Table);

// Loading and reading steps shared by the billing commands and queries.
internal sealed class BillSteps
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public BillSteps(IAppDbContext db, ICurrentUser currentUser, IClock clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public int UserId => _currentUser.UserId ?? throw new InvalidOperationException("Billing requires a signed-in user.");

    public bool IsManager => ManagerApprovals.IsManager(_currentUser);

    public bool IsWaiterOnly => _currentUser.IsInRole(Roles.Waiter) && !IsManager && !_currentUser.IsInRole(Roles.Cashier);

    public async Task<BillScope?> LoadAsync(int id, CancellationToken cancellationToken)
    {
        var bill = await _db.Bills.Include(b => b.Items).Include(b => b.Payments).FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (bill is null)
        {
            return null;
        }

        var order = await _db.Orders.Include(o => o.Table).Include(o => o.Items).ThenInclude(i => i.Modifiers)
            .FirstAsync(o => o.Id == bill.OrderId, cancellationToken);
        return new BillScope(bill, order, order.Table);
    }

    public Task<bool> RoundOffAsync(CancellationToken cancellationToken) =>
        _db.BoolSettingAsync(SettingKeys.RoundOffTotals, true, cancellationToken);

    public async Task<string> HolderAsync(Bill bill, CancellationToken cancellationToken)
    {
        if (bill.ClaimedByDeviceId is { } deviceId
            && await _db.Devices.AsNoTracking().Where(d => d.Id == deviceId).Select(d => d.Name).FirstOrDefaultAsync(cancellationToken) is { } device)
        {
            return device;
        }

        return bill.ClaimedByUserId is { } userId ? await UserNameAsync(userId, cancellationToken) ?? "another cashier" : "another cashier";
    }

    public Task<string?> UserNameAsync(int userId, CancellationToken cancellationToken) =>
        _db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => (string?)u.DisplayName).FirstOrDefaultAsync(cancellationToken);

    // The bill as the caller may see it: a waiter only sees the bills of their own orders unless any waiter may edit.
    public async Task<Result<BillDetailDto>> GetAsync(int id, CancellationToken cancellationToken)
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

    public async Task<BillDetailDto?> DetailAsync(int id, CancellationToken cancellationToken)
    {
        var bill = await _db.Bills.AsNoTracking().Include(b => b.Items).Include(b => b.Payments).FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (bill is null)
        {
            return null;
        }

        var order = await _db.Orders.AsNoTracking().Where(o => o.Id == bill.OrderId)
            .Select(o => new { o.OrderNumber, o.TableId, o.GuestCount, o.OrderType })
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
            OrderType = order.OrderType,
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
            TaxBreakup = BillTaxBreakup.For(bill.Items, split),
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
            CustomerLegalName = bill.CustomerLegalName,
            CustomerStateCode = bill.CustomerStateCode,
            PlaceOfSupply = bill.PlaceOfSupply,
            SupplyType = bill.SupplyType,
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

    public async Task<IReadOnlyList<BillSummaryDto>> SummariesAsync(IQueryable<Bill> bills, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var rows = await bills.Select(b => new
        {
            b.Id,
            b.BillNumber,
            b.InvoiceNumber,
            b.OrderId,
            OrderNumber = _db.Orders.Where(o => o.Id == b.OrderId).Select(o => o.OrderNumber).FirstOrDefault(),
            OrderType = _db.Orders.Where(o => o.Id == b.OrderId).Select(o => o.OrderType).FirstOrDefault(),
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
                OrderType = r.OrderType,
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
}
