using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common;
using HotelPOS.Application.Features.Reports;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Cash;

namespace HotelPOS.Application.Features.DayEnd;

// The day's report pack: the existing reports as CSV (the same files the Reports screen exports), plus the shifts and
// non-till collections. Phase 14 adds the PDF and Excel versions.
internal sealed class DayEndPack
{
    private readonly IBusinessDays _businessDays;
    private readonly IReportQueries _reports;
    private readonly IAppDbContext _db;

    public DayEndPack(IBusinessDays businessDays, IReportQueries reports, IAppDbContext db)
    {
        _businessDays = businessDays;
        _reports = reports;
        _db = db;
    }

    public async Task<IReadOnlyList<DayEndPackFile>> BuildAsync(DateOnly day, DayEndSummaryDto summary, CancellationToken cancellationToken)
    {
        var window = ReportRanges.Window(await _businessDays.GetAsync(cancellationToken), day, day);
        var lateMinutes = await _db.IntSettingAsync(SettingKeys.KitchenLateMinutes, 20, cancellationToken);
        var stamp = day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        string Name(string report) => $"{report}-{stamp}.csv";

        return new[]
        {
            new DayEndPackFile(Name("daily-sales"), ReportCsv.Write(await _reports.DailySalesAsync(window, cancellationToken))),
            new DayEndPackFile(Name("payments"), ReportCsv.Write(await _reports.PaymentsAsync(window, cancellationToken))),
            new DayEndPackFile(Name("items"), ReportCsv.Write(await _reports.ItemSalesAsync(window, cancellationToken))),
            new DayEndPackFile(Name("categories"), ReportCsv.Write(await _reports.CategorySalesAsync(window, cancellationToken))),
            new DayEndPackFile(Name("taxes"), ReportCsv.Write(await _reports.TaxesAsync(window, cancellationToken))),
            new DayEndPackFile(Name("discounts"), ReportCsv.Write(await _reports.DiscountsAsync(window, cancellationToken))),
            new DayEndPackFile(Name("cancellations"), ReportCsv.Write(await _reports.CancellationsAsync(window, cancellationToken))),
            new DayEndPackFile(Name("waiters"), ReportCsv.Write(await _reports.WaitersAsync(window, cancellationToken))),
            new DayEndPackFile(Name("kitchen"), ReportCsv.Write(await _reports.KitchenAsync(window, lateMinutes, cancellationToken))),
            new DayEndPackFile(Name("shifts"), ReportCsv.Write(summary.Shifts)),
            new DayEndPackFile(Name("non-till-collections"), ReportCsv.Write(summary.NonTillCollections)),
        };
    }
}
