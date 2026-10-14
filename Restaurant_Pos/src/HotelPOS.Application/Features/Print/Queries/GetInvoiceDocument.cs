using System.Globalization;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Features.Billing;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Print;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Billing;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Print.Queries;

// Managers, cashiers and waiters: the endpoint gates by role, there is no permission. A waiter only prints the
// invoices of their own orders unless any waiter may edit orders.
public sealed record GetInvoiceDocumentQuery(int BillId) : IQuery<InvoiceDocument>;

internal sealed class GetInvoiceDocumentHandler : IQueryHandler<GetInvoiceDocumentQuery, InvoiceDocument>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly PrintSources _sources;

    public GetInvoiceDocumentHandler(IAppDbContext db, ICurrentUser currentUser, IClock clock, PrintSources sources)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _sources = sources;
    }

    private bool IsDesk => _currentUser.IsInRole(Roles.Cashier) || _currentUser.IsInRole(Roles.Manager) || _currentUser.IsInRole(Roles.Admin);

    public async Task<Result<InvoiceDocument>> Handle(GetInvoiceDocumentQuery request, CancellationToken cancellationToken)
    {
        var billId = request.BillId;
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
        var settings = await _sources.SettingsAsync(cancellationToken);
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
            Address = PrintSources.Blank(settings.GetValueOrDefault(SettingKeys.Address)),
            Phone = PrintSources.Blank(settings.GetValueOrDefault(SettingKeys.Phone)),
            Gstin = PrintSources.Blank(settings.GetValueOrDefault(SettingKeys.Gstin)),
            CurrencySymbol = settings.GetValueOrDefault(SettingKeys.CurrencySymbol) ?? string.Empty,
            TableCode = bill.TableCode,
            OrderNumber = order.OrderNumber,
            WaiterName = await _sources.UserNameAsync(bill.WaiterId, cancellationToken),
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
            TaxRows = BillTaxBreakup.For(bill.Items, settings.GetValueOrDefault(SettingKeys.TaxSplitDisplay))
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
            Footer = PrintSources.Blank(settings.GetValueOrDefault(SettingKeys.ReceiptFooter)),
            Copies = copies,
        };
    }

    private static string DiscountLabel(Bill bill, string? discountName)
    {
        var name = discountName ?? bill.DiscountReason ?? "Discount";
        return bill.DiscountType == DiscountType.Percentage
            ? string.Create(CultureInfo.InvariantCulture, $"{name} {bill.DiscountValue:0.##}%")
            : name;
    }
}
