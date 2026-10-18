using HotelPOS.Contracts.Reports;

namespace HotelPOS.Application.Features.Reports;

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
    public int ReservationsToday { get; init; }
    public decimal ExpensesToday { get; init; }
}

// Phase 22 F4: live expenses summed per category for a range of business days (ExpenseDate is a calendar date, so the
// range is From..To inclusive with no shift).
public sealed record ExpenseCategoryTotal
{
    public string Category { get; init; } = string.Empty;
    public decimal Amount { get; init; }
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

    Task<IReadOnlyList<HourlySalesRowDto>> HourlySalesAsync(ReportWindow window, CancellationToken cancellationToken);

    Task<IReadOnlyList<MonthlySalesRowDto>> MonthlySalesAsync(ReportWindow window, CancellationToken cancellationToken);

    Task<IReadOnlyList<ItemSalesRowDto>> ItemSalesAsync(ReportWindow window, CancellationToken cancellationToken);

    Task<IReadOnlyList<CategorySalesRowDto>> CategorySalesAsync(ReportWindow window, CancellationToken cancellationToken);

    Task<IReadOnlyList<PaymentSummaryRowDto>> PaymentsAsync(ReportWindow window, CancellationToken cancellationToken);

    Task<IReadOnlyList<CancelledOrderRowDto>> CancellationsAsync(ReportWindow window, CancellationToken cancellationToken);

    Task<IReadOnlyList<DiscountRowDto>> DiscountsAsync(ReportWindow window, CancellationToken cancellationToken);

    Task<IReadOnlyList<TaxCollectionRowDto>> TaxesAsync(ReportWindow window, CancellationToken cancellationToken);

    Task<IReadOnlyList<ExpenseCategoryTotal>> ExpensesByCategoryAsync(ReportWindow window, CancellationToken cancellationToken);

    Task<IReadOnlyList<WaiterPerformanceRowDto>> WaitersAsync(ReportWindow window, CancellationToken cancellationToken);

    Task<IReadOnlyList<KitchenPerformanceRowDto>> KitchenAsync(ReportWindow window, int lateMinutes, CancellationToken cancellationToken);
}
