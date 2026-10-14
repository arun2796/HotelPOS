using System.Globalization;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Features.Reports;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Gst;
using HotelPOS.Domain.Gst;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Gst;

// One rate group of an invoice or note: taxable value and the tax in its heads.
internal sealed record GstRateLine(decimal Rate, decimal Taxable, GstSplit Tax);

internal sealed record GstInvoice(
    int BillId,
    string Number,
    DateOnly Date,
    decimal Value,
    GstSupplyType Type,
    string? Ctin,
    string CustomerName,
    string? CustomerState,
    string Pos,
    bool InterState,
    IReadOnlyList<GstRateLine> Lines)
{
    public decimal Taxable => Lines.Sum(l => l.Taxable);

    public GstSplit Tax => Lines.Aggregate(default(GstSplit), (sum, l) => sum + l.Tax);
}

// A credit note for one refund of a settled bill.
internal sealed record GstNote(
    int PaymentId,
    string Number,
    DateOnly Date,
    string InvoiceNumber,
    decimal Value,
    GstSupplyType InvoiceType,
    string? Ctin,
    string Pos,
    bool InterState,
    IReadOnlyList<GstRateLine> Lines)
{
    public decimal Taxable => Lines.Sum(l => l.Taxable);

    public GstSplit Tax => Lines.Aggregate(default(GstSplit), (sum, l) => sum + l.Tax);
}

internal sealed record GstHsnLine(string Hsn, decimal Rate, int Quantity, decimal Taxable, GstSplit Tax);

internal sealed record GstDocumentRange(string Document, int DocNum, string From, string To, int Total, int Cancelled);

// Everything GSTR-1, GSTR-3B, the HSN summary and the validation report are built from, read once for one month.
internal sealed record GstPeriodData(
    DateOnly Start,
    DateOnly End,
    GstSettings Settings,
    IReadOnlyList<GstInvoice> Invoices,
    IReadOnlyList<GstNote> Notes,
    IReadOnlyList<GstHsnLine> Hsn,
    IReadOnlyList<GstDocumentRange> Documents,
    IReadOnlyList<GstValidationRowDto> Issues)
{
    public string Key => Periods.Key(PeriodKind.Month, Start);

    public string FilingPeriod => Periods.FilingPeriod(Start);
}

internal sealed class GstDataLoader
{
    private const decimal Tolerance = 0.01m;

    private readonly IAppDbContext _db;
    private readonly IBusinessDays _businessDays;
    private readonly IReportQueries _reports;

    public GstDataLoader(IAppDbContext db, IBusinessDays businessDays, IReportQueries reports)
    {
        _db = db;
        _businessDays = businessDays;
        _reports = reports;
    }

    // A bill header with its items, as far as GST needs them.
    private sealed record BillRow(
        int Id,
        string? InvoiceNumber,
        int BillNumber,
        DateTime? SettledAt,
        decimal GrandTotal,
        decimal TaxAmount,
        string? CustomerGstin,
        string? CustomerName,
        string? CustomerLegalName,
        string? CustomerStateCode,
        string? PlaceOfSupply,
        GstSupplyType? SupplyType);

    private sealed record ItemRow(int BillId, decimal Rate, decimal LineSubtotal, decimal DiscountShare, decimal TaxAmount, int Quantity, string? Hsn);

    public async Task<GstPeriodData> LoadAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken)
    {
        var settings = await GstSettings.ReadAsync(_db, cancellationToken);
        var calendar = await _businessDays.GetAsync(cancellationToken);
        var window = ReportRanges.Window(calendar, start, end);
        var issues = new List<GstValidationRowDto>();

        if (settings.Gstin.Length == 0)
        {
            issues.Add(Issue(GstIssueSeverity.Error, GstIssueCodes.RestaurantGstinMissing, "Settings", "Enter the restaurant GSTIN in Settings (it names the state of supply and heads the return)."));
        }
        else if (!settings.GstinValid)
        {
            issues.Add(Issue(GstIssueSeverity.Error, GstIssueCodes.RestaurantGstinInvalid, settings.Gstin, Domain.Gst.Gstin.Validate(settings.Gstin) ?? "The restaurant GSTIN is invalid."));
        }

        var bills = _db.Bills.AsNoTracking().Where(b => b.Status == BillStatus.Settled && b.SettledAt >= window.FromUtc && b.SettledAt < window.ToUtc);
        var billRows = await ReadBillsAsync(bills, cancellationToken);
        var items = await ReadItemsAsync(bills, cancellationToken);
        var itemsByBill = items.ToLookup(i => i.BillId);

        var invoices = new List<GstInvoice>();
        var hsn = new List<GstHsnLine>();
        foreach (var bill in billRows.OrderBy(b => b.SettledAt).ThenBy(b => b.BillNumber))
        {
            var invoice = Build(bill, itemsByBill[bill.Id].ToList(), settings, calendar, issues);
            invoices.Add(invoice);
            hsn.AddRange(HsnOf(itemsByBill[bill.Id], settings, invoice.InterState));
        }

        var notes = await BuildNotesAsync(window, settings, calendar, cancellationToken);
        var documents = await DocumentsAsync(window, invoices, notes, cancellationToken);

        var pending = await _db.Bills.AsNoTracking()
            .Where(b => (b.Status == BillStatus.Open || b.Status == BillStatus.Finalized) && b.CreatedAt < window.ToUtc)
            .OrderBy(b => b.BillNumber).Select(b => b.BillNumber).Take(20).ToListAsync(cancellationToken);
        if (pending.Count > 0)
        {
            issues.Add(Issue(GstIssueSeverity.Warning, GstIssueCodes.PendingBills, string.Join(", ", pending.Select(n => "Bill " + n)),
                "These bills are not settled yet, so they are not in this return. Settle or void them before closing the period."));
        }

        var sales = await _reports.DailySalesAsync(window, cancellationToken);
        var salesGross = sales.Sum(s => s.GrossSales);
        var salesTax = sales.Sum(s => s.Tax);
        if (Math.Abs(salesGross - invoices.Sum(i => i.Value)) > Tolerance)
        {
            issues.Add(Issue(GstIssueSeverity.Error, GstIssueCodes.SalesTotalMismatch, Periods.Key(PeriodKind.Month, start),
                $"Invoice value in the return is {invoices.Sum(i => i.Value):0.00} but the sales report shows gross sales of {salesGross:0.00}."));
        }

        if (Math.Abs(salesTax - invoices.Sum(i => i.Tax.Total)) > Tolerance)
        {
            issues.Add(Issue(GstIssueSeverity.Error, GstIssueCodes.SalesTaxMismatch, Periods.Key(PeriodKind.Month, start),
                $"Tax in the return is {invoices.Sum(i => i.Tax.Total):0.00} but the sales report shows tax of {salesTax:0.00}."));
        }

        return new GstPeriodData(start, end, settings, invoices, notes, hsn, documents, issues);
    }

    private async Task<List<BillRow>> ReadBillsAsync(IQueryable<Domain.Billing.Bill> bills, CancellationToken cancellationToken) =>
        await bills.Select(b => new BillRow(b.Id, b.InvoiceNumber, b.BillNumber, b.SettledAt, b.GrandTotal, b.TaxAmount, b.CustomerGstin,
            b.CustomerName, b.CustomerLegalName, b.CustomerStateCode, b.PlaceOfSupply, b.SupplyType)).ToListAsync(cancellationToken);

    private async Task<List<ItemRow>> ReadItemsAsync(IQueryable<Domain.Billing.Bill> bills, CancellationToken cancellationToken) =>
        await (from b in bills
               join bi in _db.BillItems.AsNoTracking() on b.Id equals bi.BillId
               join oi in _db.OrderItems.AsNoTracking() on bi.OrderItemId equals oi.Id
               join mi in _db.MenuItems.AsNoTracking() on oi.MenuItemId equals mi.Id
               select new ItemRow(b.Id, bi.TaxRatePercent, bi.LineSubtotal, bi.DiscountShare, bi.TaxAmount, bi.Quantity, mi.HsnSac))
            .ToListAsync(cancellationToken);

    private static GstInvoice Build(BillRow bill, List<ItemRow> items, GstSettings settings, BusinessDayCalculator calendar, List<GstValidationRowDto> issues)
    {
        var number = bill.InvoiceNumber ?? string.Empty;
        if (bill.InvoiceNumber is null)
        {
            issues.Add(Issue(GstIssueSeverity.Error, GstIssueCodes.InvoiceNumberMissing, $"Bill {bill.BillNumber}", "A settled bill has no invoice number."));
        }

        var ctin = bill.CustomerGstin is not null && Domain.Gst.Gstin.IsValid(bill.CustomerGstin) ? bill.CustomerGstin : null;
        if (bill.CustomerGstin is not null && ctin is null)
        {
            issues.Add(Issue(GstIssueSeverity.Warning, GstIssueCodes.CustomerGstinInvalid, number,
                $"Customer GSTIN {bill.CustomerGstin} is not valid, so the invoice is reported as unregistered (B2C)."));
        }

        if (ctin is not null && bill.CustomerStateCode is not null && bill.CustomerStateCode != ctin[..2])
        {
            issues.Add(Issue(GstIssueSeverity.Warning, GstIssueCodes.CustomerStateMismatch, number,
                $"The customer state {GstStates.Label(bill.CustomerStateCode)} does not match the GSTIN state {GstStates.Label(ctin[..2])}."));
        }

        var pos = bill.PlaceOfSupply ?? settings.StateCode ?? string.Empty;
        var inter = GstSupply.IsInterState(settings.StateCode, pos);
        var type = bill.SupplyType is { } stored && (stored != GstSupplyType.B2B || ctin is not null)
            ? stored
            : GstSupply.Classify(ctin is not null, inter, bill.GrandTotal, settings.B2clThreshold);
        var date = calendar.DayOf(bill.SettledAt!.Value);
        var name = bill.CustomerLegalName ?? bill.CustomerName ?? string.Empty;
        return new GstInvoice(bill.Id, number, date, bill.GrandTotal, type, ctin, name, bill.CustomerStateCode, pos, inter, RateLines(items, inter));
    }

    private static IReadOnlyList<GstRateLine> RateLines(IEnumerable<ItemRow> items, bool inter) => items
        .GroupBy(i => i.Rate)
        .OrderBy(g => g.Key)
        .Select(g => new GstRateLine(g.Key, g.Sum(i => i.LineSubtotal - i.DiscountShare), GstSplit.Of(g.Sum(i => i.TaxAmount), inter)))
        .ToList();

    private static IEnumerable<GstHsnLine> HsnOf(IEnumerable<ItemRow> items, GstSettings settings, bool inter) => items
        .GroupBy(i => (Hsn: i.Hsn ?? settings.DefaultSac, i.Rate))
        .Select(g => new GstHsnLine(g.Key.Hsn, g.Key.Rate, g.Sum(i => i.Quantity), g.Sum(i => i.LineSubtotal - i.DiscountShare), GstSplit.Of(g.Sum(i => i.TaxAmount), inter)));

    // Each refund of a settled bill dated in the period is a credit note, for the share of the bill it returns.
    private async Task<List<GstNote>> BuildNotesAsync(ReportWindow window, GstSettings settings, BusinessDayCalculator calendar, CancellationToken cancellationToken)
    {
        var refunds = await _db.Payments.AsNoTracking()
            .Where(p => p.RefundOfPaymentId != null && p.PaidAt >= window.FromUtc && p.PaidAt < window.ToUtc)
            .OrderBy(p => p.PaidAt).ThenBy(p => p.Id)
            .Select(p => new { p.Id, p.BillId, p.Amount, p.PaidAt })
            .ToListAsync(cancellationToken);
        if (refunds.Count == 0)
        {
            return new List<GstNote>();
        }

        var billIds = refunds.Select(r => r.BillId).Distinct().ToList();
        var bills = _db.Bills.AsNoTracking().Where(b => billIds.Contains(b.Id));
        var rows = (await ReadBillsAsync(bills, cancellationToken)).ToDictionary(b => b.Id);
        var items = (await ReadItemsAsync(bills, cancellationToken)).ToLookup(i => i.BillId);
        var notes = new List<GstNote>();
        foreach (var refund in refunds)
        {
            var bill = rows[refund.BillId];
            if (bill.GrandTotal <= 0)
            {
                continue;
            }

            var invoice = Build(bill, items[bill.Id].ToList(), settings, calendar, new List<GstValidationRowDto>());
            var share = -refund.Amount / bill.GrandTotal;
            var lines = invoice.Lines.Select(l =>
                new GstRateLine(l.Rate, Money(l.Taxable * share), GstSplit.Of(Money(l.Tax.Total * share), invoice.InterState))).ToList();
            var date = calendar.DayOf(refund.PaidAt);
            notes.Add(new GstNote(refund.Id, "CN-" + refund.Id.ToString("000000", CultureInfo.InvariantCulture), date, invoice.Number, -refund.Amount,
                invoice.Type, invoice.Ctin, invoice.Pos, invoice.InterState, lines));
        }

        return notes;
    }

    // Invoice number ranges per series (the number without its trailing digits), cancelled invoices counted, and the
    // credit notes issued.
    private async Task<List<GstDocumentRange>> DocumentsAsync(ReportWindow window, List<GstInvoice> invoices, List<GstNote> notes, CancellationToken cancellationToken)
    {
        var voided = await _db.Bills.AsNoTracking()
            .Where(b => b.Status == BillStatus.Voided && b.InvoiceNumber != null && b.VoidedAt >= window.FromUtc && b.VoidedAt < window.ToUtc)
            .Select(b => b.InvoiceNumber!).ToListAsync(cancellationToken);
        var issued = invoices.Select(i => (Number: i.Number, Cancelled: false)).Concat(voided.Select(v => (Number: v, Cancelled: true)))
            .Where(i => i.Number.Length > 0).ToList();
        var ranges = new List<GstDocumentRange>();
        foreach (var series in issued.GroupBy(i => Series(i.Number)).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var ordered = series.OrderBy(i => i.Number.Length).ThenBy(i => i.Number, StringComparer.Ordinal).ToList();
            ranges.Add(new GstDocumentRange("Invoices for outward supply", 1, ordered[0].Number, ordered[^1].Number, ordered.Count, ordered.Count(i => i.Cancelled)));
        }

        if (notes.Count > 0)
        {
            ranges.Add(new GstDocumentRange("Credit note", 5, notes[0].Number, notes[^1].Number, notes.Count, 0));
        }

        return ranges;
    }

    private static string Series(string number) => number.TrimEnd("0123456789".ToCharArray());

    public static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static GstValidationRowDto Issue(string severity, string code, string reference, string message) =>
        new() { Severity = severity, Code = code, Reference = reference, Message = message };
}
