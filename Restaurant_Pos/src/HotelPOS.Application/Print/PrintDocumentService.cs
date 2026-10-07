using System.Globalization;
using HotelPOS.Application.Billing;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Print;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Billing;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Print;

public interface IPrintDocumentService
{
    Task<Result<KitchenTicketDocument>> BuildKotAsync(int ticketId, CancellationToken cancellationToken = default);

    Task<Result<InvoiceDocument>> BuildInvoiceAsync(int billId, CancellationToken cancellationToken = default);

    Task<Result<ReceiptDocument>> BuildReceiptAsync(int paymentId, CancellationToken cancellationToken = default);

    Task<Result> RecordReprintAsync(ReprintRequest request, CancellationToken cancellationToken = default);
}

// Documents are composed here, from persisted rows and the Settings table, so every terminal prints the same thing.
public sealed class PrintDocumentService : IPrintDocumentService
{
    private readonly IAppDbContext _db;
    private readonly IAuditService _audit;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public PrintDocumentService(IAppDbContext db, IAuditService audit, ICurrentUser currentUser, IClock clock)
    {
        _db = db;
        _audit = audit;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<KitchenTicketDocument>> BuildKotAsync(int ticketId, CancellationToken cancellationToken = default)
    {
        var ticket = await _db.KitchenOrders.AsNoTracking()
            .Include(k => k.Order).ThenInclude(o => o!.Table)
            .Include(k => k.Items).ThenInclude(i => i.OrderItem).ThenInclude(oi => oi!.Modifiers)
            .FirstOrDefaultAsync(k => k.Id == ticketId, cancellationToken);
        if (ticket?.Order is null)
        {
            return AppErrors.NotFound("Ticket", ticketId);
        }

        var order = ticket.Order;
        var station = await _db.PreparationStations.AsNoTracking()
            .Where(s => s.Id == ticket.PreparationStationId)
            .Select(s => new { s.Code, s.Name })
            .FirstOrDefaultAsync(cancellationToken);
        var settings = await SettingsAsync(cancellationToken);
        var showPrices = string.Equals(settings.GetValueOrDefault(SettingKeys.KotShowPrices), "true", StringComparison.OrdinalIgnoreCase);

        return new KitchenTicketDocument
        {
            TicketId = ticket.Id,
            TicketNumber = ticket.TicketNumber,
            OrderNumber = order.OrderNumber,
            BatchNumber = ticket.BatchNumber,
            IsAddition = ticket.BatchNumber > 1,
            TableCode = order.Table?.Code ?? string.Empty,
            WaiterName = await UserNameAsync(order.WaiterId, cancellationToken),
            StationCode = station?.Code ?? string.Empty,
            StationName = station?.Name ?? string.Empty,
            GuestCount = order.GuestCount,
            OrderNotes = order.Notes,
            CreatedAtUtc = ticket.CreatedAt,
            PrintedAtUtc = _clock.UtcNow,
            ShowPrices = showPrices,
            CurrencySymbol = settings.GetValueOrDefault(SettingKeys.CurrencySymbol) ?? string.Empty,
            Items = ticket.Items.OrderBy(i => i.Id).Select(i => new KotLine
            {
                Name = i.OrderItem?.ItemName ?? string.Empty,
                Quantity = i.Quantity,
                Notes = i.OrderItem?.Notes,
                Modifiers = i.OrderItem?.Modifiers.Select(m => m.Name).ToList() ?? new List<string>(),
                IsCancelled = i.IsCancelled,
                UnitPrice = showPrices ? i.OrderItem?.UnitPrice : null,
            }).ToList(),
        };
    }

    public async Task<Result<InvoiceDocument>> BuildInvoiceAsync(int billId, CancellationToken cancellationToken = default)
    {
        var bill = await _db.Bills.AsNoTracking().Include(b => b.Items).Include(b => b.Payments)
            .FirstOrDefaultAsync(b => b.Id == billId, cancellationToken);
        if (bill is null)
        {
            return AppErrors.NotFound("Bill", billId);
        }

        if (_currentUser.IsInRole(Roles.Waiter) && !IsDesk && bill.WaiterId != _currentUser.UserId
            && !await _db.BoolSettingAsync(SettingKeys.AllowAnyWaiterToEditOrders, false, cancellationToken))
        {
            return AppErrors.Forbidden("This invoice belongs to another waiter's order.");
        }

        var order = await _db.Orders.AsNoTracking().Where(o => o.Id == bill.OrderId)
            .Select(o => new { o.OrderNumber, o.GuestCount })
            .FirstAsync(cancellationToken);
        var settings = await SettingsAsync(cancellationToken);
        var methods = await _db.PaymentMethods.AsNoTracking().ToDictionaryAsync(m => m.Id, m => m.Name, cancellationToken);
        var discountName = bill.DiscountId is { } discountId
            ? await _db.Discounts.AsNoTracking().Where(d => d.Id == discountId).Select(d => d.Name).FirstOrDefaultAsync(cancellationToken)
            : null;
        var copies = int.TryParse(settings.GetValueOrDefault(SettingKeys.PrintInvoiceCopies), NumberStyles.Integer, CultureInfo.InvariantCulture, out var c)
            ? Math.Clamp(c, 1, PrintLimits.MaxCopies)
            : 1;

        return new InvoiceDocument
        {
            BillId = bill.Id,
            BillNumber = bill.BillNumber,
            InvoiceNumber = bill.InvoiceNumber,
            IssuedAtUtc = bill.FinalizedAt ?? bill.CreatedAt,
            PrintedAtUtc = _clock.UtcNow,
            RestaurantName = settings.GetValueOrDefault(SettingKeys.RestaurantName) ?? string.Empty,
            Address = Blank(settings.GetValueOrDefault(SettingKeys.Address)),
            Phone = Blank(settings.GetValueOrDefault(SettingKeys.Phone)),
            Gstin = Blank(settings.GetValueOrDefault(SettingKeys.Gstin)),
            CurrencySymbol = settings.GetValueOrDefault(SettingKeys.CurrencySymbol) ?? string.Empty,
            TableCode = bill.TableCode,
            OrderNumber = order.OrderNumber,
            WaiterName = await UserNameAsync(bill.WaiterId, cancellationToken),
            GuestCount = order.GuestCount,
            Customer = bill.CustomerName is null && bill.CustomerPhone is null && bill.CustomerGstin is null
                ? null
                : new InvoiceCustomer { Name = bill.CustomerName, Phone = bill.CustomerPhone, Gstin = bill.CustomerGstin },
            Lines = bill.Items.OrderBy(i => i.Id).Select(i => new InvoiceLine
            {
                Name = i.ItemName,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice + i.ModifiersAmount,
                LineSubtotal = i.LineSubtotal,
                TaxRatePercent = i.TaxRatePercent,
            }).ToList(),
            Subtotal = bill.Subtotal,
            DiscountLabel = bill.DiscountAmount > 0 ? DiscountLabel(bill, discountName) : null,
            DiscountAmount = bill.DiscountAmount,
            TaxableAmount = bill.TaxableAmount,
            TaxRows = BillingService.TaxBreakup(bill.Items, settings.GetValueOrDefault(SettingKeys.TaxSplitDisplay))
                .Select(t => new TaxRow { Label = t.Label, RatePercent = t.RatePercent, TaxableAmount = t.TaxableAmount, TaxAmount = t.TaxAmount })
                .ToList(),
            TaxAmount = bill.TaxAmount,
            RoundOff = bill.RoundOff,
            GrandTotal = bill.GrandTotal,
            Payments = bill.Payments.OrderBy(p => p.PaidAt).ThenBy(p => p.Id).Select(p => new InvoicePaymentLine
            {
                Method = methods.GetValueOrDefault(p.PaymentMethodId, string.Empty),
                Amount = p.Amount,
                Reference = p.Reference,
                IsRefund = p.IsRefund,
            }).ToList(),
            PaidAmount = bill.PaidAmount,
            BalanceDue = bill.BalanceDue,
            IsCancelled = bill.Status == BillStatus.Voided,
            CancelReason = bill.VoidReason,
            Footer = Blank(settings.GetValueOrDefault(SettingKeys.ReceiptFooter)),
            Copies = copies,
        };
    }

    public async Task<Result<ReceiptDocument>> BuildReceiptAsync(int paymentId, CancellationToken cancellationToken = default)
    {
        var payment = await _db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.Id == paymentId, cancellationToken);
        if (payment is null)
        {
            return AppErrors.NotFound("Payment", paymentId);
        }

        var bill = await _db.Bills.AsNoTracking().FirstAsync(b => b.Id == payment.BillId, cancellationToken);
        var orderNumber = await _db.Orders.AsNoTracking().Where(o => o.Id == bill.OrderId).Select(o => o.OrderNumber).FirstAsync(cancellationToken);
        var settings = await SettingsAsync(cancellationToken);
        var method = await _db.PaymentMethods.AsNoTracking().Where(m => m.Id == payment.PaymentMethodId).Select(m => m.Name).FirstOrDefaultAsync(cancellationToken);

        return new ReceiptDocument
        {
            PaymentId = payment.Id,
            BillId = bill.Id,
            InvoiceNumber = bill.InvoiceNumber,
            RestaurantName = settings.GetValueOrDefault(SettingKeys.RestaurantName) ?? string.Empty,
            CurrencySymbol = settings.GetValueOrDefault(SettingKeys.CurrencySymbol) ?? string.Empty,
            TableCode = bill.TableCode,
            OrderNumber = orderNumber,
            Method = method ?? string.Empty,
            Amount = Math.Abs(payment.Amount),
            TenderedAmount = payment.TenderedAmount,
            ChangeAmount = payment.ChangeAmount,
            Reference = payment.Reference,
            IsRefund = payment.IsRefund,
            RefundReason = payment.RefundReason,
            PaidAtUtc = payment.PaidAt,
            PrintedAtUtc = _clock.UtcNow,
            ReceivedBy = await UserNameAsync(payment.ReceivedBy, cancellationToken),
            GrandTotal = bill.GrandTotal,
            PaidAmount = bill.PaidAmount - bill.RefundedAmount,
            BalanceDue = bill.BalanceDue,
            Footer = Blank(settings.GetValueOrDefault(SettingKeys.ReceiptFooter)),
        };
    }

    public async Task<Result> RecordReprintAsync(ReprintRequest request, CancellationToken cancellationToken = default)
    {
        var (entityType, exists, label) = request.DocumentType switch
        {
            PrintDocumentType.Invoice => ("Bill", await _db.Bills.AnyAsync(b => b.Id == request.EntityId, cancellationToken), "Invoice"),
            PrintDocumentType.Receipt => ("Payment", await _db.Payments.AnyAsync(p => p.Id == request.EntityId, cancellationToken), "Receipt"),
            _ => ("KitchenTicket", await _db.KitchenOrders.AnyAsync(k => k.Id == request.EntityId, cancellationToken), "Kitchen ticket"),
        };
        if (!exists)
        {
            return AppErrors.NotFound(label, request.EntityId);
        }

        _audit.Record(AuditActions.PrintReprint, entityType, request.EntityId.ToString(CultureInfo.InvariantCulture),
            newValues: new { request.DocumentType, request.Reason, Device = _currentUser.DeviceName });
        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private bool IsDesk => _currentUser.IsInRole(Roles.Cashier) || _currentUser.IsInRole(Roles.Manager) || _currentUser.IsInRole(Roles.Admin);

    private Task<Dictionary<string, string>> SettingsAsync(CancellationToken cancellationToken) =>
        _db.Settings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);

    private async Task<string> UserNameAsync(int userId, CancellationToken cancellationToken) =>
        await _db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.DisplayName).FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

    private static string DiscountLabel(Bill bill, string? discountName)
    {
        var name = discountName ?? bill.DiscountReason ?? "Discount";
        return bill.DiscountType == DiscountType.Percentage
            ? string.Create(CultureInfo.InvariantCulture, $"{name} {bill.DiscountValue:0.##}%")
            : name;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
