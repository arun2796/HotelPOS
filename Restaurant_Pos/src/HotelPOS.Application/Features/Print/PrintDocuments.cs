using System.Globalization;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Features.Billing;
using HotelPOS.Application.Features.Templates;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Print;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Billing;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Print;

// Builds the render model of every printed document from persisted rows, the Settings table and the template in
// force (Phase 7 documents, extended in Phase 14). The template changes presentation only: amounts, tax and numbers
// always come from the bill.
internal sealed class PrintDocuments
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly PrintSources _sources;
    private readonly DocumentTemplates _templates;
    private readonly IQrCodeRenderer _qr;
    private readonly ILogoStore _logos;

    public PrintDocuments(
        IAppDbContext db,
        ICurrentUser currentUser,
        IClock clock,
        PrintSources sources,
        DocumentTemplates templates,
        IQrCodeRenderer qr,
        ILogoStore logos)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _sources = sources;
        _templates = templates;
        _qr = qr;
        _logos = logos;
    }

    private bool IsDesk => _currentUser.IsInRole(Roles.Cashier) || _currentUser.IsInRole(Roles.Manager) || _currentUser.IsInRole(Roles.Admin);

    public async Task<Result<object>> BuildAsync(PrintDocumentType type, int id, CancellationToken cancellationToken) => type switch
    {
        PrintDocumentType.Kot => Box(await KotAsync(id, null, cancellationToken)),
        PrintDocumentType.Invoice => Box(await InvoiceAsync(id, null, cancellationToken)),
        _ => Box(await ReceiptAsync(id, null, cancellationToken)),
    };

    // The copy fields of a reserved copy, on any of the three documents.
    public static object WithCopy(object document, int copyNumber, long logId) => document switch
    {
        KitchenTicketDocument kot => kot with { CopyNumber = copyNumber, CopyLabel = PrintCopies.LabelFor(copyNumber), PrintLogId = logId },
        InvoiceDocument invoice => invoice with { CopyNumber = copyNumber, CopyLabel = PrintCopies.LabelFor(copyNumber), PrintLogId = logId },
        ReceiptDocument receipt => receipt with { CopyNumber = copyNumber, CopyLabel = PrintCopies.LabelFor(copyNumber), PrintLogId = logId },
        _ => document,
    };

    public async Task<Result<KitchenTicketDocument>> KotAsync(int ticketId, TemplateLayout? layout, CancellationToken cancellationToken)
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
        var settings = await _sources.SettingsAsync(cancellationToken);
        var template = await TemplateAsync(PrintDocumentType.Kot, layout, cancellationToken);
        var showPrices = template.Layout.KotShowPrices
            ?? string.Equals(settings.GetValueOrDefault(SettingKeys.KotShowPrices), "true", StringComparison.OrdinalIgnoreCase);

        return new KitchenTicketDocument
        {
            Template = template.Layout,
            TemplateVersion = template.Version,
            TicketId = ticket.Id,
            TicketNumber = ticket.TicketNumber,
            OrderNumber = order.OrderNumber,
            BatchNumber = ticket.BatchNumber,
            IsAddition = ticket.BatchNumber > 1,
            TableCode = order.Table?.Code ?? string.Empty,
            WaiterName = await _sources.UserNameAsync(order.WaiterId, cancellationToken),
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

    // Managers, cashiers and waiters. A waiter only prints the invoices of their own orders unless any waiter may edit
    // orders.
    public async Task<Result<InvoiceDocument>> InvoiceAsync(int billId, TemplateLayout? layout, CancellationToken cancellationToken)
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
        var settings = await _sources.SettingsAsync(cancellationToken);
        var template = await TemplateAsync(PrintDocumentType.Invoice, layout, cancellationToken);
        var methods = await _db.PaymentMethods.AsNoTracking().ToDictionaryAsync(m => m.Id, m => m.Name, cancellationToken);
        var discountName = bill.DiscountId is { } discountId
            ? await _db.Discounts.AsNoTracking().Where(d => d.Id == discountId).Select(d => d.Name).FirstOrDefaultAsync(cancellationToken)
            : null;
        var copies = int.TryParse(settings.GetValueOrDefault(SettingKeys.PrintInvoiceCopies), NumberStyles.Integer, CultureInfo.InvariantCulture, out var c)
            ? Math.Clamp(c, 1, PrintLimits.MaxCopies)
            : 1;
        var items = bill.Items.OrderBy(i => i.Id).ToList();

        var document = new InvoiceDocument
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
            Lines = items.Select(i => new InvoiceLine
            {
                Name = i.ItemName,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice + i.ModifiersAmount,
                LineSubtotal = i.LineSubtotal,
                TaxRatePercent = i.TaxRatePercent,
                DiscountAmount = i.DiscountShare,
                TaxAmount = i.TaxAmount,
            }).ToList(),
            Subtotal = bill.Subtotal,
            DiscountLabel = bill.DiscountAmount > 0 ? DiscountLabel(bill, discountName) : null,
            DiscountAmount = bill.DiscountAmount,
            TaxableAmount = bill.TaxableAmount,
            TaxRows = TaxRows(items, bill, template.Layout.GstBreakup, settings.GetValueOrDefault(SettingKeys.TaxSplitDisplay)),
            TaxRates = TaxRates(items),
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

        return Decorate(document, template, settings);
    }

    public async Task<Result<ReceiptDocument>> ReceiptAsync(int paymentId, TemplateLayout? layout, CancellationToken cancellationToken)
    {
        var payment = await _db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.Id == paymentId, cancellationToken);
        if (payment is null)
        {
            return AppErrors.NotFound("Payment", paymentId);
        }

        var bill = await _db.Bills.AsNoTracking().FirstAsync(b => b.Id == payment.BillId, cancellationToken);
        var orderNumber = await _db.Orders.AsNoTracking().Where(o => o.Id == bill.OrderId).Select(o => o.OrderNumber).FirstAsync(cancellationToken);
        var settings = await _sources.SettingsAsync(cancellationToken);
        var template = await TemplateAsync(PrintDocumentType.Receipt, layout, cancellationToken);
        var method = await _db.PaymentMethods.AsNoTracking().Where(m => m.Id == payment.PaymentMethodId).Select(m => m.Name).FirstOrDefaultAsync(cancellationToken);

        return new ReceiptDocument
        {
            Template = template.Layout,
            TemplateVersion = template.Version,
            LogoPng = Logo(template.Layout, settings),
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
            ReceivedBy = await _sources.UserNameAsync(payment.ReceivedBy, cancellationToken),
            GrandTotal = bill.GrandTotal,
            PaidAmount = bill.PaidAmount - bill.RefundedAmount,
            BalanceDue = bill.BalanceDue,
            Footer = PrintSources.Blank(settings.GetValueOrDefault(SettingKeys.ReceiptFooter)),
        };
    }

    // Sample documents for the Template Studio preview (no bill chosen): realistic lines, the restaurant's own header.
    public async Task<object> SampleAsync(PrintDocumentType type, TemplateLayout layout, CancellationToken cancellationToken)
    {
        var settings = await _sources.SettingsAsync(cancellationToken);
        var template = new TemplateInForce(layout, null);
        var now = _clock.UtcNow;
        var name = settings.GetValueOrDefault(SettingKeys.RestaurantName) ?? "Restaurant";
        var symbol = settings.GetValueOrDefault(SettingKeys.CurrencySymbol) ?? string.Empty;
        var footer = PrintSources.Blank(settings.GetValueOrDefault(SettingKeys.ReceiptFooter));
        switch (type)
        {
            case PrintDocumentType.Kot:
                var showPrices = layout.KotShowPrices
                    ?? string.Equals(settings.GetValueOrDefault(SettingKeys.KotShowPrices), "true", StringComparison.OrdinalIgnoreCase);
                return new KitchenTicketDocument
                {
                    Template = layout,
                    TicketNumber = "1025-1",
                    OrderNumber = 1025,
                    BatchNumber = 1,
                    TableCode = "T05",
                    WaiterName = "Arun",
                    StationCode = "MAIN",
                    StationName = "Main Kitchen",
                    GuestCount = 4,
                    OrderNotes = "Birthday table",
                    CreatedAtUtc = now,
                    PrintedAtUtc = now,
                    ShowPrices = showPrices,
                    CurrencySymbol = symbol,
                    Items = new[]
                    {
                        new KotLine { Name = "Chicken Biryani", Quantity = 2, Notes = "less oil", Modifiers = new[] { "Spicy" }, UnitPrice = showPrices ? 260m : null },
                        new KotLine { Name = "Butter Naan", Quantity = 4, UnitPrice = showPrices ? 50m : null },
                        new KotLine { Name = "Paneer Tikka", Quantity = 1, UnitPrice = showPrices ? 240m : null },
                    },
                };
            case PrintDocumentType.Receipt:
                return new ReceiptDocument
                {
                    Template = layout,
                    LogoPng = Logo(layout, settings),
                    InvoiceNumber = "INV-SAMPLE-0001",
                    RestaurantName = name,
                    CurrencySymbol = symbol,
                    TableCode = "T04",
                    OrderNumber = 1019,
                    Method = "Cash",
                    Amount = 964m,
                    TenderedAmount = 1000m,
                    ChangeAmount = 36m,
                    PaidAtUtc = now,
                    PrintedAtUtc = now,
                    ReceivedBy = _currentUser.UserName ?? "Cashier",
                    GrandTotal = 964m,
                    PaidAmount = 964m,
                    Footer = footer,
                };
            default:
                var lines = new[]
                {
                    new InvoiceLine { Name = "Chicken Biryani", Quantity = 2, UnitPrice = 260m, LineSubtotal = 520m, TaxRatePercent = 5m, DiscountAmount = 78m, TaxAmount = 22.1m },
                    new InvoiceLine { Name = "Butter Naan", Quantity = 4, UnitPrice = 50m, LineSubtotal = 200m, TaxRatePercent = 5m, DiscountAmount = 30m, TaxAmount = 8.5m },
                    new InvoiceLine { Name = "Fresh Lime Soda", Quantity = 3, UnitPrice = 60m, LineSubtotal = 180m, TaxRatePercent = 5m, DiscountAmount = 27m, TaxAmount = 7.65m },
                    new InvoiceLine { Name = "Gulab Jamun", Quantity = 2, UnitPrice = 90m, LineSubtotal = 180m, TaxRatePercent = 5m, DiscountAmount = 27m, TaxAmount = 7.65m },
                };
                var split = layout.GstBreakup == GstBreakupStyle.SplitRows || layout.GstBreakup == GstBreakupStyle.RateTable
                    || (layout.GstBreakup == GstBreakupStyle.FollowSetting && string.Equals(settings.GetValueOrDefault(SettingKeys.TaxSplitDisplay), "CGST_SGST", StringComparison.OrdinalIgnoreCase));
                var taxRows = layout.GstBreakup == GstBreakupStyle.SingleLine
                    ? new[] { new TaxRow { Label = "GST", RatePercent = 5m, TaxableAmount = 918m, TaxAmount = 45.9m } }
                    : split
                        ? new[]
                        {
                            new TaxRow { Label = "CGST 2.5%", RatePercent = 2.5m, TaxableAmount = 918m, TaxAmount = 22.95m },
                            new TaxRow { Label = "SGST 2.5%", RatePercent = 2.5m, TaxableAmount = 918m, TaxAmount = 22.95m },
                        }
                        : new[] { new TaxRow { Label = "GST 5%", RatePercent = 5m, TaxableAmount = 918m, TaxAmount = 45.9m } };
                var sample = new InvoiceDocument
                {
                    BillNumber = 42,
                    InvoiceNumber = "INV-SAMPLE-0001",
                    IssuedAtUtc = now,
                    PrintedAtUtc = now,
                    RestaurantName = name,
                    Address = PrintSources.Blank(settings.GetValueOrDefault(SettingKeys.Address)),
                    Phone = PrintSources.Blank(settings.GetValueOrDefault(SettingKeys.Phone)),
                    Gstin = PrintSources.Blank(settings.GetValueOrDefault(SettingKeys.Gstin)) ?? "33ABCDE1234F1Z5",
                    CurrencySymbol = symbol,
                    TableCode = "T04",
                    OrderNumber = 1019,
                    WaiterName = "Arun",
                    GuestCount = 4,
                    Customer = new InvoiceCustomer { Name = "Ravi Kumar", Phone = "98400 12345" },
                    Lines = lines,
                    Subtotal = 1080m,
                    DiscountLabel = "Regular 15%",
                    DiscountAmount = 162m,
                    TaxableAmount = 918m,
                    TaxRows = taxRows,
                    TaxRates = new[] { new TaxRateRow { RatePercent = 5m, TaxableAmount = 918m, CentralTax = 22.95m, StateTax = 22.95m, TaxAmount = 45.9m } },
                    TaxAmount = 45.9m,
                    RoundOff = 0.10m,
                    GrandTotal = 964m,
                    PaidAmount = 0m,
                    BalanceDue = 964m,
                    Footer = footer,
                };
                return Decorate(sample, template, settings);
        }
    }

    private async Task<TemplateInForce> TemplateAsync(PrintDocumentType type, TemplateLayout? layout, CancellationToken cancellationToken) =>
        layout is null ? await _templates.ActiveAsync(type, cancellationToken) : new TemplateInForce(layout, null);

    // Template-driven additions: HSN/SAC codes, the UPI payment QR and the logo.
    private InvoiceDocument Decorate(InvoiceDocument document, TemplateInForce template, IReadOnlyDictionary<string, string> settings)
    {
        var layout = template.Layout;
        var lines = document.Lines;
        var hsn = Array.Empty<HsnSummaryRow>() as IReadOnlyList<HsnSummaryRow>;
        if (layout.Columns.Contains(TemplateColumn.HsnSac))
        {
            var sac = PrintSources.Blank(settings.GetValueOrDefault(SettingKeys.DefaultSacCode));
            lines = lines.Select(l => l with { HsnSac = l.HsnSac ?? sac }).ToList();
            hsn = lines.Where(l => l.HsnSac is not null && l.TaxRatePercent > 0)
                .GroupBy(l => (l.HsnSac!, l.TaxRatePercent))
                .OrderBy(g => g.Key.Item1, StringComparer.Ordinal).ThenBy(g => g.Key.TaxRatePercent)
                .Select(g =>
                {
                    var taxable = g.Sum(l => l.LineSubtotal - l.DiscountAmount);
                    return new HsnSummaryRow
                    {
                        HsnSac = g.Key.Item1,
                        RatePercent = g.Key.TaxRatePercent,
                        TaxableAmount = taxable,
                        TaxAmount = g.Sum(l => l.TaxAmount),
                    };
                })
                .ToList();
        }

        string? payload = null;
        byte[]? qr = null;
        var vpa = PrintSources.Blank(settings.GetValueOrDefault(SettingKeys.UpiVpa));
        if (layout.ShowUpiQr && vpa is not null && !document.IsCancelled && document.BalanceDue > 0)
        {
            var payee = PrintSources.Blank(settings.GetValueOrDefault(SettingKeys.UpiPayeeName)) ?? document.RestaurantName;
            payload = UpiPayload(vpa, payee, document.BalanceDue, document.InvoiceNumber ?? "Bill " + document.BillNumber.ToString(CultureInfo.InvariantCulture));
            qr = _qr.Png(payload);
        }

        return document with
        {
            Template = layout,
            TemplateVersion = template.Version,
            Lines = lines,
            HsnSummary = hsn,
            UpiQrPayload = payload,
            UpiQrPng = qr,
            LogoPng = Logo(layout, settings),
        };
    }

    // upi://pay?pa=<vpa>&pn=<payee>&am=<amount>&cu=INR&tn=<invoice>, as read by every UPI app.
    public static string UpiPayload(string vpa, string payee, decimal amount, string note) =>
        "upi://pay?pa=" + Uri.EscapeDataString(vpa)
        + "&pn=" + Uri.EscapeDataString(payee)
        + "&am=" + amount.ToString("0.00", CultureInfo.InvariantCulture)
        + "&cu=INR"
        + "&tn=" + Uri.EscapeDataString(note);

    private byte[]? Logo(TemplateLayout layout, IReadOnlyDictionary<string, string> settings) =>
        layout.ShowLogo ? _logos.Read(PrintSources.Blank(settings.GetValueOrDefault(SettingKeys.LogoFile))) : null;

    private static IReadOnlyList<TaxRow> TaxRows(IReadOnlyList<BillItem> items, Bill bill, GstBreakupStyle style, string? splitSetting)
    {
        if (style == GstBreakupStyle.SingleLine)
        {
            return bill.TaxAmount == 0
                ? Array.Empty<TaxRow>()
                : new[] { new TaxRow { Label = "GST", RatePercent = 0m, TaxableAmount = bill.TaxableAmount, TaxAmount = bill.TaxAmount } };
        }

        var mode = style is GstBreakupStyle.SplitRows or GstBreakupStyle.RateTable ? "CGST_SGST" : splitSetting;
        return BillTaxBreakup.For(items, mode)
            .Select(t => new TaxRow { Label = t.Label, RatePercent = t.RatePercent, TaxableAmount = t.TaxableAmount, TaxAmount = t.TaxAmount })
            .ToList();
    }

    private static IReadOnlyList<TaxRateRow> TaxRates(IReadOnlyList<BillItem> items)
    {
        // The halves come from the same split as the CGST/SGST rows, so the table and the rows always agree.
        var split = BillTaxBreakup.For(items, "CGST_SGST");
        return split.Chunk(2).Select(pair => new TaxRateRow
        {
            RatePercent = pair[0].RatePercent * 2,
            TaxableAmount = pair[0].TaxableAmount,
            CentralTax = pair[0].TaxAmount,
            StateTax = pair.Length > 1 ? pair[1].TaxAmount : 0m,
            TaxAmount = pair.Sum(p => p.TaxAmount),
        }).ToList();
    }

    private static string DiscountLabel(Bill bill, string? discountName)
    {
        var name = discountName ?? bill.DiscountReason ?? "Discount";
        return bill.DiscountType == DiscountType.Percentage
            ? string.Create(CultureInfo.InvariantCulture, $"{name} {bill.DiscountValue:0.##}%")
            : name;
    }

    private static Result<object> Box<T>(Result<T> result)
        where T : class =>
        result.IsSuccess ? Result<object>.Ok(result.Value) : Result<object>.Failure(result.Error!);
}
