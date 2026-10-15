using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common;
using HotelPOS.Application.Features.Exports;
using HotelPOS.Application.Features.Reports;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Cash;

namespace HotelPOS.Application.Features.DayEnd;

// The day's report pack: the existing reports as CSV (the same files the Reports screen exports), plus the shifts and
// non-till collections; Phase 14 adds one Excel workbook (a sheet per report) and one PDF on the restaurant letterhead.
internal sealed class DayEndPack
{
    private readonly IBusinessDays _businessDays;
    private readonly IReportQueries _reports;
    private readonly IAppDbContext _db;
    private readonly IExcelExporter _excel;
    private readonly IPdfRenderer _pdf;
    private readonly ILogoStore _logos;
    private readonly IClock _clock;

    public DayEndPack(IBusinessDays businessDays, IReportQueries reports, IAppDbContext db, IExcelExporter excel, IPdfRenderer pdf, ILogoStore logos, IClock clock)
    {
        _businessDays = businessDays;
        _reports = reports;
        _db = db;
        _excel = excel;
        _pdf = pdf;
        _logos = logos;
        _clock = clock;
    }

    public async Task<IReadOnlyList<DayEndPackFile>> BuildAsync(DateOnly day, DayEndSummaryDto summary, CancellationToken cancellationToken)
    {
        var window = ReportRanges.Window(await _businessDays.GetAsync(cancellationToken), day, day);
        var lateMinutes = await _db.IntSettingAsync(SettingKeys.KitchenLateMinutes, 20, cancellationToken);
        var stamp = day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var subtitle = day.ToString("dd-MM-yyyy", System.Globalization.CultureInfo.InvariantCulture);
        var generated = _clock.UtcNow.ToLocalTime();
        var files = new List<DayEndPackFile>();
        var tables = new List<ReportTable>();

        void Add<T>(string file, string title, IReadOnlyList<T> rows)
        {
            files.Add(new DayEndPackFile($"{file}-{stamp}.csv", ReportCsv.Write(rows)));
            tables.Add(ReportTables.From(title, subtitle, rows, generated));
        }

        Add("daily-sales", "Daily sales", await _reports.DailySalesAsync(window, cancellationToken));
        Add("payments", "Payments", await _reports.PaymentsAsync(window, cancellationToken));
        Add("items", "Item sales", await _reports.ItemSalesAsync(window, cancellationToken));
        Add("categories", "Category sales", await _reports.CategorySalesAsync(window, cancellationToken));
        Add("taxes", "Taxes", await _reports.TaxesAsync(window, cancellationToken));
        Add("discounts", "Discounts", await _reports.DiscountsAsync(window, cancellationToken));
        Add("cancellations", "Cancellations", await _reports.CancellationsAsync(window, cancellationToken));
        Add("waiters", "Waiters", await _reports.WaitersAsync(window, cancellationToken));
        Add("kitchen", "Kitchen", await _reports.KitchenAsync(window, lateMinutes, cancellationToken));
        Add("shifts", "Shifts", summary.Shifts);
        Add("non-till-collections", "Non-till collections", summary.NonTillCollections);

        var letterhead = await ReportTables.LetterheadAsync(_db, _logos, cancellationToken);
        files.Add(new DayEndPackFile($"day-end-{stamp}.xlsx", _excel.Workbook(tables, letterhead)));
        files.Add(new DayEndPackFile($"day-end-{stamp}.pdf", _pdf.Reports(tables, letterhead)));
        return files;
    }
}
