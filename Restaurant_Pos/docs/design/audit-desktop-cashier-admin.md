# Audit: Cashier and Admin mock-ups vs the existing desktop

Planning only (2026-10-17). Mock-ups: `docs/design/reference/cashier-workspace.png`, `admin-dashboard.png`. Compared
against `src/HotelPOS.Desktop` at a3f2676 and the gallery in `docs/ui-ux/desktop/`. No builds.

## 1. Key decision: where does the Admin/Manager UI live?

Facts:

- Every Admin/Manager and Cashier screen is WPF: 30 modules in `Services/Navigation/ModuleRegistry.cs`, 65 XAML files, view-models of 300–1,000 lines (`Modules/Billing/BillDetailViewModel.cs` is 990).
- The browser client (`HotelPOS.Web` + `HotelPOS.Client.Shared`) is a phone/tablet shell for waiters and kitchen: bottom nav in `Layout/MainLayout.razor`, `ShellMode` = `Waiter | Kitchen | None`, and `Pages/Home.razor` tells other roles "Use the desktop application". It has auth, refresh, SignalR, toasts, theme and a `tokens.css` mirroring the desktop palette, but no sidebar layout, grid, charts, or typed clients beyond `WaiterApi`/`KitchenApi`.
- The API already serves every admin need with permission checks (`docs/03-api-reference.md` §3.7–3.13) and hosts `/app` same-origin, so a browser admin needs no backend change.
- The desktop has the chart primitives (`Controls/ChartGeometry.cs` ring; bars in `Modules/Dashboard/DashboardView.xaml`), a token contract test (`tests/HotelPOS.Desktop.Tests/ThemeTokenContractTests.cs`: same keys both themes, no stray hex) and a 73-screen render test.

| Option | Scope | Agent-days | Verdict |
|---|---|---|---|
| **A. Restyle WPF** | Navy sidebar + regrouped nav, card/KPI restyle, new dashboard widgets from existing endpoints, cashier three-panel workspace | Cashier 6–8, Admin 9–12, tests/docs 2 → **17–22** | Everything the data supports is reachable in one code base; token swap is nearly free. Admin stays a Windows app. |
| **B. Blazor admin in Client.Shared** | Desktop-width sidebar layout and ~24 pages to match what Admin/Manager see today (dashboard, tables, orders, kitchen, billing ×3, menu ×5 tabs, 10 reports, GST hub, period close, shifts, day end ×2, users, roles, devices, settings + Template Studio, audit, backup, server, licence, import, printers). Needs typed clients for billing/reports/admin/gst/shifts/menu-admin, a grid, charts (hand-rolled SVG suffices; a library is a new package Phase 17 avoided), file downloads, and a browser story for machine-bound pages (printers, template preview, backup paths, licence machine id). Cashier stays WPF anyway. | Dashboard + reports + GST only 12–16; parity **45–60**, then two admin code bases | The dashboard is cheap; parity is not. "Only show supported pages" would leave a browser admin with 6–8 pages plus a desktop for the rest. |
| **C. Hybrid** | Dashboard, Reports, GST in browser; the rest restyled WPF | 12–16 + 10–12 → **24–28** | Two shells and navigations; managers switch apps mid-task. |

**Recommendation: A, with the "browser" line raised as decision D1.** The mock-up's look (navy sidebar, cards, pills, donut, bars, table) is a styling problem WPF solves with tokens and four new panels. The hard gaps (hourly sales, order types, expenses, P&L, floor geometry, staff presence, inventory) are data gaps a browser does not close. If a browser admin is mandatory, do A first (it yields the widget logic and the gap list), then B's dashboard subset later.

## 2. Cashier gap table

Today: queue `Modules/Billing/BillingQueueView.xaml` (cards + OPEN), bill `BillDetailView.xaml` (lines | totals, CASH/CARD/UPI/SPLIT, FINALIZE/REOPEN/VOID; discount, customer and payment as overlays), `PaymentViewModel.cs` (amount, tendered, quick tenders Exact/50/100/500/2000, change, numpad, reference, split lines, idempotency key per line, `PendingRequest` retry). Gallery `cashier/04`, `05`, `07`, `08`.

| Element | Exists (file) | Gap |
|---|---|---|
| Three panels bills \| order \| payment | No: two pages, payment overlay (`BillDetailView.xaml` l.335–420) | **Layout (L)**: new `CashierWorkspaceView` hosting both VMs |
| Search table / bill / item / customer | Ctrl+K palette finds bill numbers; queue has no box | Layout; item/customer **needs data** (`BillSummaryDto` has table, order, waiter, totals only) |
| Chips Dine In / Takeaway / Delivery | No order type in `Contracts/Orders` or `Domain` | **Needs data**: show "All bills" only |
| Chips Open / Partial / Ready / Paid | `BillStatus` Open/Finalized + `PaymentStatus` on pending; Paid is `ClosedBillsView.xaml` | **Decision**: Waiting / Invoiced / Partly paid; "Paid" opens Closed Bills |
| Sort newest first | Oldest-first by design | Style (S) toggle |
| Card: #order · table, pill, guests, waiter, items, total, time | Table, order, waiter, wait minutes, total, claim badge; no guest/item counts on `BillSummaryDto` | Style; show what exists |
| Order header | `BillDetailView.xaml` l.54–72 | Style |
| Edit order / Add items | Cashier cannot edit; REOPEN returns the order to the waiter | **Decision**: "Reopen for waiter"; no Add items |
| Print bill / More | `FinalizeAsync`, `PreviewInvoiceAsync`, `PrintInvoiceAsync` | Style |
| Line thumbnails, options, notes | Options/notes done in 20.11; no image on `BillItemDto`, but the VM loads the order so `MenuCache.ImageFile` can be joined | Needs data (client join), optional |
| Order notes banner | Order notes exist; VM loads the order | Style (S) |
| Subtotal / Add discount / Tax / Total | Exists with real CGST/SGST/IGST and round-off | Style; keep real tax rows |
| Payment panel inline: due, quick amounts, methods, received, change, Complete | All in `PaymentViewModel`, shown as overlay; buttons hard-wired `PayCash/PayCard/PayUpi/PaySplit` although `/api/payment-methods` is data-driven | Layout (M) + **decision**: buttons from the configured list |
| Wallet | No integration; could be added as a payment-method row | Excluded / needs data |
| No fake success, no duplicate submit | `IdempotencyKey`, `PendingRequest`, `ConfirmPaymentAsync` | Exists |
| Split bill | Split **payment** by method exists; split by items/guests does not | **Decision**: label "Split payment" |
| Send to kitchen | Not a cashier action | Excluded, drop |
| Cancel order | `VoidAsync` (finalized, unpaid, approval) | Exists, wording |
| Sidebar badges | `NavItemViewModel.cs` has none; `CashierHomeViewModel` counts pending bills | Needs client count; Billing badge only |
| Device Online / Terminal / Version block | Status bar in `Shell/ShellView.xaml` | Style, move to sidebar foot |
| Shift bar (absent from mock-up) | `Modules/Cash/ShiftBarView.xaml` | **Decision**: keep; cash tender needs an open shift |

## 3. Admin gap table

### 3.1 Widgets

Today: `DashboardView.xaml`, `DashboardViewModel.cs`; `DashboardDto` (`Contracts/Reports/ReportContracts.cs`) carries today's sales, bills, orders, open orders, active/total tables, pending tickets, pending bills, avg ticket minutes, top items, last 7 days.

| Widget | Exists | Gap |
|---|---|---|
| Date-range and branch pickers | Today only; no branches | Branch **excluded**; range **needs data**, link to Reports |
| KPI Sales, Orders, Occupied tables | Six `KpiTile`s | Style (icon tile, occupancy bar) |
| KPI Average Order Value | Derivable `TodaySales / BillsToday` | **Decision** (display derivation only) |
| "vs yesterday" sparklines | `LastDays` gives yesterday's sales only | Sales delta only; no sparkline, no invented trends |
| Low Stock KPI and table | No inventory | **Excluded** |
| Live Order Status donut | Derivable from `/api/orders/active` (`OrderStatus`); "Delayed" is not a status | Needs data (client count); `ChartGeometry.RingSegment` |
| Hourly Sales | No endpoint; `DailySalesRowDto` is per day | **Needs data**; keep "Net sales · last 7 days" |
| Top dishes with images and trends | `TopItems` (name, qty, amount, bar); images via `MenuCache`; no trend | Style + optional image join |
| Active Orders table (filter, sort, paging, View) | `/api/orders/active` → `OrderSummaryDto` (number, table, waiter, guests, items, approx total, status, created); desktop has only "My Orders" (`OrderListViewModels.cs`) | New panel (M), client paging |
| Table Status cards + View Floor Plan | `Modules/Tables/TableMapView.xaml` grid by section; `Domain/Floor/Table.cs` has no x/y | Reuse `TableTileViewModel`; **no floor geometry** |
| Recent Activity | Bell / `NoticeCenter` holds hub events since sign-in; Audit Log is persisted | **Decision**: session feed vs `/api/admin/audit-logs` |
| Staff & Device Status | Terminals panel (admin only; `DeviceDto.IsOnline`, `LastSeenAtUtc`); no staff presence (`UserDto.LastLoginAtUtc` only) | Devices: style; staff **needs data**, show "Staff today" |

### 3.2 Sidebar items → module keys (`ModuleRegistry.cs`)

| Prompt item | Key | Note |
|---|---|---|
| Dashboard | `dashboard` | |
| Tables & Floor Plan | `tables` (+ `floor` editor) | no geometry |
| Orders | **does not exist** for all orders (`my-orders` is own) | new VM over `GetActiveAsync` |
| Kitchen Display | `kitchen-display`, `kitchen-completed` | |
| Cashier & Bills | `billing`, `closed-bills`, `print-log` | |
| Categories / Menu Items | `menu` tabs; Modifiers tab exists but is **excluded** | tab deep-link |
| Payments | `discounts` (methods, denominations) + Payments report | |
| Expenses | **does not exist** (nearest: pay-outs, `CashMovementPanelView.xaml`) | document |
| Cash Reconciliation | `shifts`, `day-end`, `day-end-history` | |
| Sales / Popular Items / Staff Performance | `reports` hub (Daily, Monthly, Items, Categories, Payments, Cancellations, Discounts, Tax collection, Waiter, Kitchen; `ReportsViewModel.cs` l.33–42) | report deep-link |
| Profit & Loss | **does not exist** (no cost data) | document |
| Tax Reports | `reports` Tax collection; `gst` (GSTR-1/3B/HSN); `period-close` | naming decision |
| Staff & Roles / Permissions | `users` / `roles` | |
| Shifts | `shifts` = **cash** shifts | wording |
| Terminals | `devices` | |
| Printers | `printers`, this terminal only | say so in UI |
| Kitchen Stations | `menu` Stations tab (`StationsPageView.xaml`) | new key |
| Restaurant Profile / Tax & Currency / Notifications | `settings` sections (`SettingSections.cs`: Restaurant holds name, address, GSTIN, currency symbol); Notifications section | section deep-link |
| Payment Methods | `discounts` tab + UPI settings | |
| Receipt Templates | `settings` Templates tab = `TemplateStudioView.xaml` | |
| Not in prompt | `customers` (excluded), `import`, `license`, `backup`, `server`, `this-device`, `audit` | decision: "System" group |

## 4. Token mapping (`Themes/Tokens.Light.xaml` → prompt)

| Key | Today | Prompt | Change |
|---|---|---|---|
| `Brush.Background` | #F4F7FA | #F4F7FB | trivial |
| `Brush.Border` | #E3E8EF | #E2E8F0 | trivial |
| `Brush.TextPrimary` / `TextSecondary` | #172033 / #667085 | #172B4D / #64748B | swap |
| `Brush.Primary` / `PrimaryHover` / `Focus` / `Link` | #078B45 / #06763B | #078747 (hover #066B38) | swap |
| `Success` / `Warning` / `Danger` / `Info` | #16A34A / #F59E0B / #DC2626 / #2563EB | same | none |
| `Brush.Sidebar*` | white sidebar, green active text | navy #142235; text #C7D0DC, muted #7C8BA1, hover #1C2D45, selected = primary + white text, divider #243650 | swap; add `SidebarGroupLabel`, `SidebarBadge` to **both** files |
| `Font.Body` (`DesignSystem.xaml` l.13) | Segoe UI Variable → Segoe UI → Inter | Inter | reorder; render test already accepts Inter |
| `Brush.Chart.*`, `Brush.Status.*` | fixed | unspecified | keep (shared with web and kitchen) |

Dark theme: `Tokens.Dark.xaml` keeps the keys; the sidebar goes navy in both themes (Phase 11 did the same with near-black); page #0F1623, surface #161F2E, primary #22B45F stay for AA. Apply the same five edits to `Client.Shared/wwwroot/css/tokens.css`. White-on-green active rows pass AA (4.6:1) only at #078747; do not lighten.

## 5. Steps (option A)

**Cashier**

| # | Step | Files | Size |
|---|---|---|---|
| C1 | Tokens + Inter first | `Tokens.Light.xaml`, `Tokens.Dark.xaml`, `DesignSystem.xaml`, `tokens.css` | S |
| C2 | Shell: navy sidebar, group labels, active row, device/version foot, badge on `NavItemViewModel` | `Shell/ShellView.xaml`, `ShellViewModel.cs`, `NavItemViewModel.cs`, `ModuleRegistry.cs` | M |
| C3 | Workspace page: queue (420 px) \| bill \| payment (320 px); `billing` key points at it; queue VM gains search/sort/chips | new `Modules/Billing/CashierWorkspaceView.xaml(.cs)` + VM, `BillingQueueViewModel.cs` | L |
| C4 | Bill cards: pill, waiter, time, total | `BillingQueueView.xaml`, `BillQueueItemViewModel` | M |
| C5 | Order column: pills, thumbnails, notes banner, totals, actions | `BillDetailView.xaml`, `BillDetailViewModel.cs` | M |
| C6 | Payment column permanent: due hero, quick amounts, configured methods, received, change, Complete, inline Paid state | `PaymentViewModel.cs`, `BillDetailView.xaml` | M |
| C7 | Quick actions (Print bill, Split payment, Void) | `CashierWorkspaceView.xaml` | S |
| C8 | `ScreenRenderingTests` (+1), `BillingViewModelTests`, `LiveFlowScreenshots`; `docs/07` §5.1 | tests, docs | S |

**Admin**

| # | Step | Files | Size |
|---|---|---|---|
| A1 | Regroup/rename nav; new keys Categories, Items, Stations, Orders (all) | `ModuleRegistry.cs`, `ShellViewModel.BuildNav`, `OrderListViewModels.cs`, `docs/07` §1 | M |
| A2 | Header (business day, Online pill); KPI cards with icon, occupancy bar, derived AOV, sales delta | `DashboardView.xaml`, `DashboardViewModel.cs`, `Controls/KpiTile.cs`, `DesignSystem.xaml` | M |
| A3 | Order-status donut from `IOrdersApi.GetActiveAsync` | `DashboardViewModel.cs`, `DashboardView.xaml` | M |
| A4 | Active orders table: chips, sort, 10/page, View → Tables | same | M |
| A5 | Table status cards via `TableTileViewModel`; "View floor plan" → `tables` | `DashboardView.xaml` | M |
| A6 | Recent activity from `NoticeCenter` (last 8), "View all" → bell/audit | `DashboardViewModel.cs`, `Services/Ui/NoticeCenter.cs` | M |
| A7 | Staff today + Terminals side by side; keep 7-day bars, payments donut, top items (+thumbnails) | `DashboardView.xaml` | S |
| A8 | Deep links: Reports by key, Settings by section/Templates, Menu by tab (`NavigateToAsync(module, parameter)` exists) | `ReportsViewModel.cs`, `SettingsHubViewModel.cs`, `MenuAdminViewModel.cs` | M |
| A9 | 1366×768 pass: five KPI columns, panel heights, Compact density | `DashboardView.xaml` | S |
| A10 | Render/token tests, admin live run, `docs/ui-ux/desktop/README.md` | tests, docs | S |

Order: C1 → C2 → A1 → C3–C7 → A2–A8 → A9 → C8/A10. Total 17–22 agent-days.

## 6. Risks and owner decisions

- **D1 Browser admin.** None exists. Accept A, or fund B (45–60 days for parity; 12–16 for a dashboard-only page that links to the desktop).
- **D2 Customers.** Module and bill picker exist and B2B invoices need a GSTIN; the prompt excludes Customers. Hide the nav item, keep the bill-level panel.
- **D3 Tax naming.** Suggest Analytics → Tax Reports (report), Finance → GST Returns (hub), Period Close under System.
- **D4 Pages missing from the prompt's nav** (Licence, Backup, Server, This Terminal, Import, Audit, Day End) cannot be dropped; propose a "System" group.
- **D5 Payment buttons** from the configured list instead of hard-wired CASH/CARD/UPI: a behaviour change, not a calculation change.
- **D6 Shift bar** stays; cash tender fails without an open shift (`SHIFT_REQUIRED`).
- **D7 Modifiers tab**: hide, do not delete; the item editor depends on it.
- **D8 "Shifts" under People** are cash shifts; relabel or accept.
- **Risk: `BillDetailViewModel`** assumes overlay panels; a permanent payment column touches most of its state transitions (C3/C6 is the biggest step). Keep the Paid dialog as an inline state so `BillingViewModelTests` still guard duplicate submits.
- **Risk: 1366×768.** Nine panels at 1920 become five KPIs, a wrapped donut legend and stacked Active orders / Table status; A9 is a real step.
- **Risk: invented data.** Trends, hourly sales, staff presence, low stock, order types must render as "not available" or be omitted.
- **Risk: token contract test** fails on a key added to one theme only or a hard-coded navy; add keys to both files, bind with `DynamicResource`.
- **Risk: web drift.** `tokens.css` and the kitchen board share the palette; change primary/text in both.
