using HotelPOS.Application.Reports;
using HotelPOS.Contracts.Reports;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Infrastructure.Persistence;

// Raw SQL on purpose: the business-day shift and FILTER aggregates have no clean LINQ equivalent, and every
// report must aggregate inside PostgreSQL. Statuses are the enum numbers from HotelPOS.Contracts.Enums.
public sealed class ReportQueries : IReportQueries
{
    private const int BillSettled = 3;
    private const int OrderCancelled = 11;

    private readonly AppDbContext _db;

    public ReportQueries(AppDbContext db)
    {
        _db = db;
    }

    public async Task<DashboardFigures> DashboardAsync(ReportWindow today, CancellationToken cancellationToken) =>
        (await _db.Database.SqlQuery<DashboardFigures>($"""
            SELECT
              (SELECT COALESCE(SUM("GrandTotal" - "RefundedAmount"), 0) FROM "Bills"
                 WHERE "Status" = {BillSettled} AND "SettledAt" >= {today.FromUtc} AND "SettledAt" < {today.ToUtc}) AS "TodaySales",
              (SELECT COUNT(*) FROM "Bills"
                 WHERE "Status" = {BillSettled} AND "SettledAt" >= {today.FromUtc} AND "SettledAt" < {today.ToUtc})::int AS "BillsToday",
              (SELECT COUNT(*) FROM "Orders" WHERE "SubmittedAt" >= {today.FromUtc} AND "SubmittedAt" < {today.ToUtc})::int AS "OrdersToday",
              (SELECT COUNT(*) FROM "Orders" WHERE "Status" NOT IN (9, 10, 11))::int AS "OpenOrders",
              (SELECT COUNT(*) FROM "Tables" WHERE "IsActive" AND "Status" NOT IN (1, 7))::int AS "ActiveTables",
              (SELECT COUNT(*) FROM "Tables" WHERE "IsActive")::int AS "TotalTables",
              (SELECT COUNT(*) FROM "KitchenOrders" WHERE "Status" IN (1, 2, 3, 4))::int AS "PendingKitchenTickets",
              (SELECT COUNT(*) FROM "Bills" WHERE "Status" IN (1, 2))::int AS "PendingBills",
              (SELECT ROUND(COALESCE(AVG(EXTRACT(EPOCH FROM ("ReadyAt" - "CreatedAt")) / 60), 0)::numeric, 1) FROM "KitchenOrders"
                 WHERE "ReadyAt" IS NOT NULL AND "CreatedAt" >= {today.FromUtc} AND "CreatedAt" < {today.ToUtc}) AS "AverageTicketMinutes"
            """).ToListAsync(cancellationToken)).Single();

    public async Task<IReadOnlyList<DashboardTopItem>> TopItemsAsync(ReportWindow window, int count, CancellationToken cancellationToken) =>
        await _db.Database.SqlQuery<DashboardTopItem>($"""
            SELECT bi."ItemName" AS "ItemName", SUM(bi."Quantity")::int AS "Quantity", SUM(bi."LineSubtotal") AS "Amount"
            FROM "BillItems" bi
            JOIN "Bills" b ON b."Id" = bi."BillId"
            WHERE b."Status" = {BillSettled} AND b."SettledAt" >= {window.FromUtc} AND b."SettledAt" < {window.ToUtc}
            GROUP BY bi."ItemName"
            ORDER BY "Quantity" DESC, "Amount" DESC
            LIMIT {count}
            """).ToListAsync(cancellationToken);

    public async Task<WaiterFigures> WaiterDashboardAsync(ReportWindow today, int waiterId, CancellationToken cancellationToken) =>
        (await _db.Database.SqlQuery<WaiterFigures>($"""
            SELECT
              (SELECT COUNT(*) FROM "Orders" WHERE "WaiterId" = {waiterId} AND "SubmittedAt" >= {today.FromUtc} AND "SubmittedAt" < {today.ToUtc})::int AS "OrdersToday",
              (SELECT COUNT(*) FROM "Orders" WHERE "WaiterId" = {waiterId} AND "Status" NOT IN (9, 10, 11))::int AS "OpenOrders",
              (SELECT COALESCE(SUM("GrandTotal" - "RefundedAmount"), 0) FROM "Bills"
                 WHERE "WaiterId" = {waiterId} AND "Status" = {BillSettled} AND "SettledAt" >= {today.FromUtc} AND "SettledAt" < {today.ToUtc}) AS "SalesToday",
              (SELECT COUNT(*) FROM "Tables" t JOIN "Orders" o ON o."Id" = t."CurrentOrderId" WHERE o."WaiterId" = {waiterId})::int AS "TablesServing",
              (SELECT COALESCE(SUM("GuestCount"), 0) FROM "Orders"
                 WHERE "WaiterId" = {waiterId} AND "SubmittedAt" >= {today.FromUtc} AND "SubmittedAt" < {today.ToUtc})::int AS "CoversToday"
            """).ToListAsync(cancellationToken)).Single();

    public async Task<IReadOnlyList<DailySalesRowDto>> DailySalesAsync(ReportWindow window, CancellationToken cancellationToken) =>
        await _db.Database.SqlQuery<DailySalesRowDto>($"""
            SELECT ((b."SettledAt" + make_interval(mins => {window.ShiftMinutes})) AT TIME ZONE 'UTC')::date AS "Day",
                   COUNT(*)::int AS "Bills",
                   COALESCE(SUM(o."GuestCount"), 0)::int AS "Covers",
                   COALESCE(SUM(b."Subtotal"), 0) AS "Subtotal",
                   COALESCE(SUM(b."DiscountAmount"), 0) AS "Discounts",
                   COALESCE(SUM(b."TaxAmount"), 0) AS "Tax",
                   COALESCE(SUM(b."GrandTotal"), 0) AS "GrossSales",
                   COALESCE(SUM(b."RefundedAmount"), 0) AS "Refunds",
                   COALESCE(SUM(b."GrandTotal" - b."RefundedAmount"), 0) AS "NetSales"
            FROM "Bills" b
            JOIN "Orders" o ON o."Id" = b."OrderId"
            WHERE b."Status" = {BillSettled} AND b."SettledAt" >= {window.FromUtc} AND b."SettledAt" < {window.ToUtc}
            GROUP BY 1
            ORDER BY 1
            """).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<MonthlySalesRowDto>> MonthlySalesAsync(ReportWindow window, CancellationToken cancellationToken) =>
        await _db.Database.SqlQuery<MonthlySalesRowDto>($"""
            SELECT to_char((b."SettledAt" + make_interval(mins => {window.ShiftMinutes})) AT TIME ZONE 'UTC', 'YYYY-MM') AS "Month",
                   COUNT(*)::int AS "Bills",
                   COALESCE(SUM(o."GuestCount"), 0)::int AS "Covers",
                   COALESCE(SUM(b."DiscountAmount"), 0) AS "Discounts",
                   COALESCE(SUM(b."TaxAmount"), 0) AS "Tax",
                   COALESCE(SUM(b."GrandTotal"), 0) AS "GrossSales",
                   COALESCE(SUM(b."RefundedAmount"), 0) AS "Refunds",
                   COALESCE(SUM(b."GrandTotal" - b."RefundedAmount"), 0) AS "NetSales"
            FROM "Bills" b
            JOIN "Orders" o ON o."Id" = b."OrderId"
            WHERE b."Status" = {BillSettled} AND b."SettledAt" >= {window.FromUtc} AND b."SettledAt" < {window.ToUtc}
            GROUP BY 1
            ORDER BY 1
            """).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ItemSalesRowDto>> ItemSalesAsync(ReportWindow window, CancellationToken cancellationToken) =>
        await _db.Database.SqlQuery<ItemSalesRowDto>($"""
            SELECT bi."ItemName" AS "ItemName", c."Name" AS "Category",
                   SUM(bi."Quantity")::int AS "Quantity", SUM(bi."LineSubtotal") AS "Amount", 0::numeric AS "SharePercent"
            FROM "BillItems" bi
            JOIN "Bills" b ON b."Id" = bi."BillId"
            JOIN "OrderItems" oi ON oi."Id" = bi."OrderItemId"
            JOIN "MenuItems" mi ON mi."Id" = oi."MenuItemId"
            JOIN "Categories" c ON c."Id" = mi."CategoryId"
            WHERE b."Status" = {BillSettled} AND b."SettledAt" >= {window.FromUtc} AND b."SettledAt" < {window.ToUtc}
            GROUP BY bi."ItemName", c."Name"
            ORDER BY "Amount" DESC, "ItemName"
            """).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CategorySalesRowDto>> CategorySalesAsync(ReportWindow window, CancellationToken cancellationToken) =>
        await _db.Database.SqlQuery<CategorySalesRowDto>($"""
            SELECT c."Name" AS "Category", COUNT(DISTINCT mi."Id")::int AS "Items",
                   SUM(bi."Quantity")::int AS "Quantity", SUM(bi."LineSubtotal") AS "Amount", 0::numeric AS "SharePercent"
            FROM "BillItems" bi
            JOIN "Bills" b ON b."Id" = bi."BillId"
            JOIN "OrderItems" oi ON oi."Id" = bi."OrderItemId"
            JOIN "MenuItems" mi ON mi."Id" = oi."MenuItemId"
            JOIN "Categories" c ON c."Id" = mi."CategoryId"
            WHERE b."Status" = {BillSettled} AND b."SettledAt" >= {window.FromUtc} AND b."SettledAt" < {window.ToUtc}
            GROUP BY c."Name"
            ORDER BY "Amount" DESC
            """).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PaymentSummaryRowDto>> PaymentsAsync(ReportWindow window, CancellationToken cancellationToken) =>
        await _db.Database.SqlQuery<PaymentSummaryRowDto>($"""
            SELECT pm."Name" AS "Method",
                   COUNT(*) FILTER (WHERE p."RefundOfPaymentId" IS NULL)::int AS "Payments",
                   COALESCE(SUM(p."Amount") FILTER (WHERE p."RefundOfPaymentId" IS NULL), 0) AS "Amount",
                   COUNT(*) FILTER (WHERE p."RefundOfPaymentId" IS NOT NULL)::int AS "Refunds",
                   COALESCE(-SUM(p."Amount") FILTER (WHERE p."RefundOfPaymentId" IS NOT NULL), 0) AS "RefundAmount",
                   COALESCE(SUM(p."Amount"), 0) AS "Net"
            FROM "Payments" p
            JOIN "PaymentMethods" pm ON pm."Id" = p."PaymentMethodId"
            WHERE p."PaidAt" >= {window.FromUtc} AND p."PaidAt" < {window.ToUtc}
            GROUP BY pm."Name", pm."SortOrder"
            ORDER BY pm."SortOrder", pm."Name"
            """).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CancelledOrderRowDto>> CancellationsAsync(ReportWindow window, CancellationToken cancellationToken) =>
        await _db.Database.SqlQuery<CancelledOrderRowDto>($"""
            SELECT o."OrderNumber" AS "OrderNumber", t."Code" AS "TableCode", w."DisplayName" AS "WaiterName",
                   o."CancelledAt" AS "CancelledAtUtc", cb."DisplayName" AS "CancelledBy", o."CancelReason" AS "CancelReason",
                   (SELECT COALESCE(SUM(oi."Quantity"), 0) FROM "OrderItems" oi WHERE oi."OrderId" = o."Id")::int AS "Items",
                   (SELECT COALESCE(SUM(oi."UnitPrice" * oi."Quantity"), 0) FROM "OrderItems" oi WHERE oi."OrderId" = o."Id") AS "ApproxAmount"
            FROM "Orders" o
            JOIN "Tables" t ON t."Id" = o."TableId"
            JOIN "Users" w ON w."Id" = o."WaiterId"
            LEFT JOIN "Users" cb ON cb."Id" = o."CancelledBy"
            WHERE o."Status" = {OrderCancelled} AND o."CancelledAt" >= {window.FromUtc} AND o."CancelledAt" < {window.ToUtc}
            ORDER BY o."CancelledAt" DESC
            """).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<DiscountRowDto>> DiscountsAsync(ReportWindow window, CancellationToken cancellationToken) =>
        await _db.Database.SqlQuery<DiscountRowDto>($"""
            SELECT b."InvoiceNumber" AS "InvoiceNumber", b."TableCode" AS "TableCode",
                   COALESCE(d."Name", b."DiscountReason", 'Manual') AS "Discount",
                   CASE b."DiscountType" WHEN 1 THEN 'Percent' WHEN 2 THEN 'Fixed' ELSE '' END AS "Type",
                   COALESCE(b."DiscountValue", 0) AS "Value", b."Subtotal" AS "Subtotal", b."DiscountAmount" AS "Amount",
                   ap."DisplayName" AS "ApprovedBy", b."SettledAt" AS "SettledAtUtc"
            FROM "Bills" b
            LEFT JOIN "Discounts" d ON d."Id" = b."DiscountId"
            LEFT JOIN "Users" ap ON ap."Id" = b."DiscountApprovedBy"
            WHERE b."Status" = {BillSettled} AND b."DiscountAmount" > 0 AND b."SettledAt" >= {window.FromUtc} AND b."SettledAt" < {window.ToUtc}
            ORDER BY b."SettledAt" DESC
            """).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TaxCollectionRowDto>> TaxesAsync(ReportWindow window, CancellationToken cancellationToken) =>
        await _db.Database.SqlQuery<TaxCollectionRowDto>($"""
            SELECT bi."TaxRatePercent" AS "RatePercent", COUNT(DISTINCT b."Id")::int AS "Bills",
                   SUM(bi."LineSubtotal" - bi."DiscountShare") AS "TaxableAmount", SUM(bi."TaxAmount") AS "TaxAmount"
            FROM "BillItems" bi
            JOIN "Bills" b ON b."Id" = bi."BillId"
            WHERE b."Status" = {BillSettled} AND b."SettledAt" >= {window.FromUtc} AND b."SettledAt" < {window.ToUtc}
            GROUP BY bi."TaxRatePercent"
            ORDER BY bi."TaxRatePercent"
            """).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<WaiterPerformanceRowDto>> WaitersAsync(ReportWindow window, CancellationToken cancellationToken) =>
        await _db.Database.SqlQuery<WaiterPerformanceRowDto>($"""
            SELECT u."DisplayName" AS "WaiterName",
                   COUNT(b."Id")::int AS "Orders",
                   COALESCE(SUM(o."GuestCount"), 0)::int AS "Covers",
                   COALESCE(SUM(b."GrandTotal" - b."RefundedAmount"), 0) AS "NetSales",
                   CASE WHEN COUNT(b."Id") = 0 THEN 0 ELSE ROUND(SUM(b."GrandTotal") / COUNT(b."Id"), 2) END AS "AverageOrderValue",
                   (SELECT COUNT(*) FROM "Orders" c
                      WHERE c."WaiterId" = u."Id" AND c."Status" = {OrderCancelled} AND c."CancelledAt" >= {window.FromUtc} AND c."CancelledAt" < {window.ToUtc})::int AS "Cancellations"
            FROM "Users" u
            JOIN "Bills" b ON b."WaiterId" = u."Id" AND b."Status" = {BillSettled} AND b."SettledAt" >= {window.FromUtc} AND b."SettledAt" < {window.ToUtc}
            JOIN "Orders" o ON o."Id" = b."OrderId"
            GROUP BY u."Id", u."DisplayName"
            ORDER BY "NetSales" DESC
            """).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<KitchenPerformanceRowDto>> KitchenAsync(ReportWindow window, int lateMinutes, CancellationToken cancellationToken) =>
        await _db.Database.SqlQuery<KitchenPerformanceRowDto>($"""
            SELECT s."Code" AS "Station", COUNT(*)::int AS "Tickets",
                   ROUND(COALESCE(AVG(EXTRACT(EPOCH FROM (k."AcceptedAt" - k."CreatedAt")) / 60), 0)::numeric, 1) AS "AverageAcceptMinutes",
                   ROUND(COALESCE(AVG(EXTRACT(EPOCH FROM (k."ReadyAt" - k."StartedAt")) / 60), 0)::numeric, 1) AS "AveragePrepMinutes",
                   ROUND(COALESCE(AVG(EXTRACT(EPOCH FROM (k."ReadyAt" - k."CreatedAt")) / 60), 0)::numeric, 1) AS "AverageTotalMinutes",
                   COUNT(*) FILTER (WHERE k."ReadyAt" - k."CreatedAt" > make_interval(mins => {lateMinutes}))::int AS "LateTickets",
                   ROUND(100.0 * COUNT(*) FILTER (WHERE k."ReadyAt" - k."CreatedAt" > make_interval(mins => {lateMinutes})) / COUNT(*), 1) AS "LatePercent"
            FROM "KitchenOrders" k
            JOIN "PreparationStations" s ON s."Id" = k."PreparationStationId"
            WHERE k."Status" IN (4, 5) AND k."ReadyAt" IS NOT NULL AND k."CreatedAt" >= {window.FromUtc} AND k."CreatedAt" < {window.ToUtc}
            GROUP BY s."Id", s."Code", s."SortOrder"
            ORDER BY s."SortOrder", s."Code"
            """).ToListAsync(cancellationToken);
}
