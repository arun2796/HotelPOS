# Phase 8 — Reports & Dashboard

Status: Done (2026-10-08) · Depends on: Phase 6

## 1. Goal

Managers and admins get a live dashboard and a set of reports (sales, items, categories, payments,
cancellations, discounts, taxes, waiter and kitchen performance) over business-day date ranges, with
CSV export. Reports are read-only queries over settled data; they never change state.

## 2. Prerequisites

- Phase 6 (settled bills, payments) and Phase 5 (ticket timestamps). `BusinessDayStartTime` setting.

## 3. Scope

**In**
- Read-model queries (EF Core projections or Dapper SQL) with business-day boundaries.
- Dashboard endpoint and screen with auto-refresh.
- Report endpoints and screens with date range, grouping, totals, CSV export.
- Optional simple charts (bar/line) in the desktop; tables are mandatory.

**Out**
- Scheduled e-mail reports, PDF report export, accounting integrations, inventory reports.

## 4. Deliverables by project

| Project | Deliverables |
|---|---|
| Contracts | `DashboardDto` (today's sales, orders today, active tables, pending kitchen tickets, pending bills, average ticket time, top 5 items), `DailySalesRowDto`, `MonthlySalesRowDto`, `ItemSalesRowDto`, `CategorySalesRowDto`, `PaymentSummaryRowDto`, `CancelledOrderRowDto`, `DiscountRowDto`, `TaxCollectionRowDto`, `WaiterPerformanceRowDto`, `KitchenPerformanceRowDto`, `ReportQuery` (from, to, grouping) |
| Application | `IReportService` with one method per report; `IBusinessDayCalculator`; CSV writer |
| Infrastructure | `ReportQueries` (SQL/LINQ), indexes if profiling shows need (`Bills(SettledAt)`, `Payments(PaidAt)`) |
| Api | `ReportsController` (`format=csv` returns `text/csv` attachment) |
| Desktop | `DashboardView/VM` (KPI tiles, refresh every 60 s and on `PaymentCompleted`), `ReportsView/VM` (report picker, date range presets: today, yesterday, this week, this month, custom; grid with totals; **EXPORT CSV**; optional chart) |
| Tests | see § 10 |

## 5. Data model

No new transactional tables. Possible migration `Phase08_ReportIndexes` for covering indexes.

## 6. API endpoints

`docs/03-api-reference.md` § 3.9.

## 7. Real-time events

None new. Dashboard refreshes on `PaymentCompleted`, `OrderCreated`, `BillRequested`,
`KitchenTicketUpdated` (debounced 5 s) plus a 60-second timer.

## 8. Business rules

- Business day: a bill belongs to the day containing `SettledAt - BusinessDayStartTime`. Orders
  for cancellation reports use `CancelledAt`.
- Sales figures use **settled** bills only (`Bill.Status = Settled`); refunds shown as negative lines
  in payment summary and netted in "Net sales".
- Item sales count quantities from `BillItems` (not `OrderItems`) so cancelled items are excluded.
- Waiter performance: orders, covers, gross sales, average order value, cancellations, per waiter.
- Kitchen performance: tickets per station, average accept time, average prep time (start -> ready),
  average total time (created -> ready), % late (over `KitchenLateMinutes`).
- Tax collection grouped by tax rate with taxable amount and tax amount.
- Max range 366 days; CSV uses UTF-8 with BOM and the venue's local time.
- Reports are Admin/Manager only; waiters see only their own KPIs on the mini dashboard (orders
  today, sales today).

## 9. Desktop screens

| Screen | Behaviour |
|---|---|
| Dashboard (Admin/Manager) | KPI tiles: Today's Sales, Orders Today, Active Tables, Pending Kitchen Tickets, Pending Bills, Avg Ticket Time; top items list; refresh indicator |
| Dashboard (Waiter mini) | My orders today, my sales today, tables I am serving |
| Reports | Left: report list; top: date presets + custom range; centre: data grid with totals footer; **EXPORT CSV**; optional chart toggle |

## 10. Tests

- Application: business-day calculator around midnight and the configured start time; daily sales
  from seeded bills match expected totals; refunds reduce net; cancelled items excluded from item
  sales; kitchen averages computed from timestamps; range > 366 days rejected.
- Api: waiter -> 403 on reports; CSV content type and BOM; dashboard shape.
- Desktop: `ReportsViewModel` preset ranges; `DashboardViewModel` debounce.

## 11. Manual demo script

1. Settle a few bills across two business days (change system clock or seed data); verify Daily
   Sales splits them by business day.
2. Apply a discount and a refund; verify Discounts and Payment Summary rows.
3. Open Dashboard on a manager PC; settle a bill on BILLING-01: tiles update within 5 s.
4. Export Item Sales to CSV and open it in Excel.

## 12. Acceptance criteria

- [x] All listed reports and dashboard available with correct business-day handling.
- [x] CSV export works; role restrictions enforced.
- [x] Report queries run under 2 s for 90 days of data on a venue-sized database (every report is one aggregated
  SQL statement over the existing `Bills(SettledAt)` / `Payments(PaidAt)` indexes; the timing on a venue-sized
  database is still to be measured, demo step 1 with seeded history).
- [x] Definition of Done satisfied.

## 13. Risks and notes

- Use SQL-side aggregation (no client-side `ToList()` then grouping).
- If charts are added, keep the library optional (`LiveChartsCore` or `ScottPlot`); tables remain the
  deliverable.

## 14. Changes during implementation

- **Raw SQL read models.** `ReportQueries` (Infrastructure) runs one aggregated statement per report through
  `SqlQuery<T>`; the business-day grouping shifts `SettledAt` by `(local UTC offset − BusinessDayStartTime)` and
  truncates to a date inside PostgreSQL, so no rows are grouped on the client. `BusinessDayCalculator`
  (Application) does the same arithmetic for the window boundaries and the dashboard "today".
- **Waiter mini dashboard** is its own endpoint, `GET /api/reports/my-day` (any signed-in user, own figures), so
  the Admin/Manager-only dashboard stays closed to waiters. The desktop Dashboard screen shows the three waiter
  tiles or the six manager tiles depending on the role.
- **One schema for grid and CSV.** `ReportSchema` (Contracts) describes a row type's columns once (humanised
  headers, numeric flag, `[NoTotal]` for rates, averages and percentages). The CSV writer and the desktop grid
  both use it, so the export shows exactly what the screen shows, including the totals row.
- Refunds are netted on the bill's settlement day (Daily/Monthly sales, waiter performance) and on the refund's
  own date in the Payment summary; "Net sales" is `GrandTotal − RefundedAmount` of settled bills.
- Monthly sales takes `?year=` (defaults to the current business year) instead of a date range.
- Cancellation amounts are approximate (unit price × quantity, modifiers excluded) because a cancelled order was
  never billed.
- Kitchen performance counts tickets that reached Ready or Completed in the range by creation time; "late" is a
  total time above `KitchenLateMinutes`.
- Charts: the dashboard has a dependency-free 7-day net-sales bar strip; the Reports screen is table-only (the
  optional chart toggle was not added).
- Dashboard refresh: a 60-second timer plus a 5-second debounce over `PaymentCompleted`, `OrderCreated`,
  `BillRequested`, `KitchenTicketUpdated` and `OrderCancelled`.
- No migration was needed: the Phase 6 indexes on `Bills(SettledAt)` and `Payments(PaidAt)` already exist.
