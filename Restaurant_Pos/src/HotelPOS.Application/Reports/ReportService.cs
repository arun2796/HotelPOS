using System.Globalization;
using System.Text;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Reports;

namespace HotelPOS.Application.Reports;

public sealed record ReportWindow(DateOnly From, DateOnly To, DateTime FromUtc, DateTime ToUtc, int ShiftMinutes);

public sealed record DashboardFigures
{
    public decimal TodaySales { get; init; }
    public int BillsToday { get; init; }
    public int OrdersToday { get; init; }
    public int OpenOrders { get; init; }
    public int ActiveTables { get; init; }
    public int TotalTables { get; init; }
    public int PendingKitchenTickets { get; init; }
    public int PendingBills { get; init; }
    public decimal AverageTicketMinutes { get; init; }
}

public sealed record WaiterFigures
{
    public int OrdersToday { get; init; }
    public int OpenOrders { get; init; }
    public decimal SalesToday { get; init; }
    public int TablesServing { get; init; }
    public int CoversToday { get; init; }
}

// SQL lives in Infrastructure (ReportQueries); every method aggregates in the database.
public interface IReportQueries
{
    Task<DashboardFigures> DashboardAsync(ReportWindow today, CancellationToken cancellationToken);

    Task<IReadOnlyList<DashboardTopItem>> TopItemsAsync(ReportWindow window, int count, CancellationToken cancellationToken);

    Task<WaiterFigures> WaiterDashboardAsync(ReportWindow today, int waiterId, CancellationToken cancellationToken);

    Task<IReadOnlyList<DailySalesRowDto>> DailySalesAsync(ReportWindow window, CancellationToken cancellationToken);

    Task<IReadOnlyList<MonthlySalesRowDto>> MonthlySalesAsync(ReportWindow window, CancellationToken cancellationToken);

    Task<IReadOnlyList<ItemSalesRowDto>> ItemSalesAsync(ReportWindow window, CancellationToken cancellationToken);

    Task<IReadOnlyList<CategorySalesRowDto>> CategorySalesAsync(ReportWindow window, CancellationToken cancellationToken);

    Task<IReadOnlyList<PaymentSummaryRowDto>> PaymentsAsync(ReportWindow window, CancellationToken cancellationToken);

    Task<IReadOnlyList<CancelledOrderRowDto>> CancellationsAsync(ReportWindow window, CancellationToken cancellationToken);

    Task<IReadOnlyList<DiscountRowDto>> DiscountsAsync(ReportWindow window, CancellationToken cancellationToken);

    Task<IReadOnlyList<TaxCollectionRowDto>> TaxesAsync(ReportWindow window, CancellationToken cancellationToken);

    Task<IReadOnlyList<WaiterPerformanceRowDto>> WaitersAsync(ReportWindow window, CancellationToken cancellationToken);

    Task<IReadOnlyList<KitchenPerformanceRowDto>> KitchenAsync(ReportWindow window, int lateMinutes, CancellationToken cancellationToken);
}

public interface IReportService
{
    Task<DashboardDto> DashboardAsync(CancellationToken cancellationToken = default);

    Task<WaiterDashboardDto> WaiterDashboardAsync(int waiterId, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<DailySalesRowDto>>> DailySalesAsync(ReportQuery query, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<MonthlySalesRowDto>>> MonthlySalesAsync(int? year, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<ItemSalesRowDto>>> ItemSalesAsync(ReportQuery query, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<CategorySalesRowDto>>> CategorySalesAsync(ReportQuery query, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<PaymentSummaryRowDto>>> PaymentsAsync(ReportQuery query, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<CancelledOrderRowDto>>> CancellationsAsync(ReportQuery query, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<DiscountRowDto>>> DiscountsAsync(ReportQuery query, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<TaxCollectionRowDto>>> TaxesAsync(ReportQuery query, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<WaiterPerformanceRowDto>>> WaitersAsync(ReportQuery query, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<KitchenPerformanceRowDto>>> KitchenAsync(ReportQuery query, CancellationToken cancellationToken = default);
}

public sealed class ReportService : IReportService
{
    private readonly IReportQueries _queries;
    private readonly IBusinessDays _businessDays;
    private readonly IAppDbContext _db;
    private readonly IClock _clock;

    public ReportService(IReportQueries queries, IBusinessDays businessDays, IAppDbContext db, IClock clock)
    {
        _queries = queries;
        _businessDays = businessDays;
        _db = db;
        _clock = clock;
    }

    public async Task<DashboardDto> DashboardAsync(CancellationToken cancellationToken = default)
    {
        var calendar = await _businessDays.GetAsync(cancellationToken);
        var today = calendar.DayOf(_clock.UtcNow);
        var window = Window(calendar, today, today);
        var figures = await _queries.DashboardAsync(window, cancellationToken);
        var topItems = await _queries.TopItemsAsync(window, 5, cancellationToken);
        var week = await _queries.DailySalesAsync(Window(calendar, today.AddDays(-6), today), cancellationToken);
        var byDay = week.ToDictionary(r => r.Day, r => r.NetSales);

        return new DashboardDto
        {
            BusinessDay = today,
            TodaySales = figures.TodaySales,
            BillsToday = figures.BillsToday,
            OrdersToday = figures.OrdersToday,
            OpenOrders = figures.OpenOrders,
            ActiveTables = figures.ActiveTables,
            TotalTables = figures.TotalTables,
            PendingKitchenTickets = figures.PendingKitchenTickets,
            PendingBills = figures.PendingBills,
            AverageTicketMinutes = figures.AverageTicketMinutes,
            TopItems = topItems,
            LastDays = Enumerable.Range(0, 7).Select(i => today.AddDays(i - 6))
                .Select(day => new DashboardDayPoint { Day = day, NetSales = byDay.GetValueOrDefault(day) })
                .ToList(),
            GeneratedAtUtc = _clock.UtcNow,
        };
    }

    public async Task<WaiterDashboardDto> WaiterDashboardAsync(int waiterId, CancellationToken cancellationToken = default)
    {
        var calendar = await _businessDays.GetAsync(cancellationToken);
        var today = calendar.DayOf(_clock.UtcNow);
        var figures = await _queries.WaiterDashboardAsync(Window(calendar, today, today), waiterId, cancellationToken);
        return new WaiterDashboardDto
        {
            BusinessDay = today,
            OrdersToday = figures.OrdersToday,
            OpenOrders = figures.OpenOrders,
            SalesToday = figures.SalesToday,
            TablesServing = figures.TablesServing,
            CoversToday = figures.CoversToday,
            GeneratedAtUtc = _clock.UtcNow,
        };
    }

    public Task<Result<IReadOnlyList<DailySalesRowDto>>> DailySalesAsync(ReportQuery query, CancellationToken cancellationToken = default) =>
        RunAsync(query, _queries.DailySalesAsync, cancellationToken);

    public async Task<Result<IReadOnlyList<MonthlySalesRowDto>>> MonthlySalesAsync(int? year, CancellationToken cancellationToken = default)
    {
        var calendar = await _businessDays.GetAsync(cancellationToken);
        var chosen = year ?? calendar.DayOf(_clock.UtcNow).Year;
        if (chosen is < 2000 or > 2100)
        {
            return AppErrors.Validation("year", "Choose a year between 2000 and 2100.");
        }

        var rows = await _queries.MonthlySalesAsync(Window(calendar, new DateOnly(chosen, 1, 1), new DateOnly(chosen, 12, 31)), cancellationToken);
        return Result<IReadOnlyList<MonthlySalesRowDto>>.Ok(rows);
    }

    public Task<Result<IReadOnlyList<ItemSalesRowDto>>> ItemSalesAsync(ReportQuery query, CancellationToken cancellationToken = default) =>
        RunAsync<ItemSalesRowDto>(query, async (w, ct) =>
        {
            var rows = await _queries.ItemSalesAsync(w, ct);
            var total = rows.Sum(r => r.Amount);
            return rows.Select(r => r with { SharePercent = Share(r.Amount, total) }).ToList();
        }, cancellationToken);

    public Task<Result<IReadOnlyList<CategorySalesRowDto>>> CategorySalesAsync(ReportQuery query, CancellationToken cancellationToken = default) =>
        RunAsync<CategorySalesRowDto>(query, async (w, ct) =>
        {
            var rows = await _queries.CategorySalesAsync(w, ct);
            var total = rows.Sum(r => r.Amount);
            return rows.Select(r => r with { SharePercent = Share(r.Amount, total) }).ToList();
        }, cancellationToken);

    public Task<Result<IReadOnlyList<PaymentSummaryRowDto>>> PaymentsAsync(ReportQuery query, CancellationToken cancellationToken = default) =>
        RunAsync(query, _queries.PaymentsAsync, cancellationToken);

    public Task<Result<IReadOnlyList<CancelledOrderRowDto>>> CancellationsAsync(ReportQuery query, CancellationToken cancellationToken = default) =>
        RunAsync(query, _queries.CancellationsAsync, cancellationToken);

    public Task<Result<IReadOnlyList<DiscountRowDto>>> DiscountsAsync(ReportQuery query, CancellationToken cancellationToken = default) =>
        RunAsync(query, _queries.DiscountsAsync, cancellationToken);

    public Task<Result<IReadOnlyList<TaxCollectionRowDto>>> TaxesAsync(ReportQuery query, CancellationToken cancellationToken = default) =>
        RunAsync(query, _queries.TaxesAsync, cancellationToken);

    public Task<Result<IReadOnlyList<WaiterPerformanceRowDto>>> WaitersAsync(ReportQuery query, CancellationToken cancellationToken = default) =>
        RunAsync(query, _queries.WaitersAsync, cancellationToken);

    public Task<Result<IReadOnlyList<KitchenPerformanceRowDto>>> KitchenAsync(ReportQuery query, CancellationToken cancellationToken = default) =>
        RunAsync(query, async (w, ct) =>
        {
            var late = int.TryParse(await _db.SettingAsync(SettingKeys.KitchenLateMinutes, ct), NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) && minutes > 0 ? minutes : 20;
            return await _queries.KitchenAsync(w, late, ct);
        }, cancellationToken);

    private async Task<Result<IReadOnlyList<T>>> RunAsync<T>(ReportQuery query, Func<ReportWindow, CancellationToken, Task<IReadOnlyList<T>>> run, CancellationToken cancellationToken)
    {
        var calendar = await _businessDays.GetAsync(cancellationToken);
        var today = calendar.DayOf(_clock.UtcNow);
        var from = query.From ?? today;
        var to = query.To ?? from;
        if (to < from)
        {
            return AppErrors.Validation("to", "The end date is before the start date.");
        }

        if (to.DayNumber - from.DayNumber >= ReportLimits.MaxRangeDays)
        {
            return AppErrors.Validation("to", $"Choose a range of at most {ReportLimits.MaxRangeDays} days.");
        }

        return Result<IReadOnlyList<T>>.Ok(await run(Window(calendar, from, to), cancellationToken));
    }

    private static ReportWindow Window(BusinessDayCalculator calendar, DateOnly from, DateOnly to)
    {
        var (fromUtc, toUtc) = calendar.Range(from, to);
        return new ReportWindow(from, to, fromUtc, toUtc, calendar.ShiftMinutes(fromUtc));
    }

    private static decimal Share(decimal amount, decimal total) => total == 0 ? 0m : Math.Round(amount * 100m / total, 1, MidpointRounding.AwayFromZero);
}

public static class ReportCsv
{
    // UTF-8 with BOM so Excel opens ₹ and accented names correctly; a totals row closes the file.
    public static byte[] Write<T>(IReadOnlyList<T> rows)
    {
        var columns = ReportSchema.Describe<T>();
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", columns.Select(c => Quote(c.Header))));
        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(",", columns.Select(c => Quote(ReportSchema.Format(c.Property.GetValue(row))))));
        }

        if (rows.Count > 0 && columns.Any(c => c.HasTotal))
        {
            builder.AppendLine(string.Join(",", columns.Select((c, i) =>
                i == 0 ? Quote("Total") : c.HasTotal ? Quote(ReportSchema.Format(Total(rows, c))) : string.Empty)));
        }

        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(builder.ToString())).ToArray();
    }

    public static object Total<T>(IReadOnlyList<T> rows, ReportColumn column)
    {
        var type = Nullable.GetUnderlyingType(column.Property.PropertyType) ?? column.Property.PropertyType;
        if (type == typeof(int) || type == typeof(long))
        {
            return rows.Sum(r => Convert.ToInt64(column.Property.GetValue(r) ?? 0, CultureInfo.InvariantCulture));
        }

        return rows.Sum(r => Convert.ToDecimal(column.Property.GetValue(r) ?? 0m, CultureInfo.InvariantCulture));
    }

    private static string Quote(string value) =>
        value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
}
