# Desktop client: the Phase 21 cashier and manager screens against the real API

Captured on **17 Oct 2026** on branch `phase-21-role-ui-ux` from the real WPF client against the running Development
API on `http://localhost:5100` (demo seed), 1366×800, light theme unless the name says `dark`. Nothing here is sample
data: every screen is the real view bound to the real view-model, fed by the real `ApiClient`, `RealtimeClient` and
session; only the machine-bound services are replaced (settings and the secure store in memory; file picker, app
lifetime and sound player are stubs). The run created order #1015 on T02 (3 lines, kitchen ready, served, bill
requested), invoice INV-202610-000013, a shift with a 3,000 float, a 450 pay-out and a close count 50 short.

How: the opt-in xUnit test `tests/HotelPOS.Desktop.Tests/Rendering/LiveFlowScreenshots.cs` builds the desktop
composition root (`AppHost.ConfigureServices`), signs in through the real login screen, drives the flow through the
view-models (`CashierWorkspaceViewModel.SelectAsync`, `PayHotkeyCommand("F2")`, `PaySplitCommand`,
`SelectFilterCommand(Paid)`, `ShellViewModel.IsSidebarCollapsed`, `MenuAdminViewModel` tab deep link) and renders each
screen with the same `RenderTargetBitmap` method as `ScreenRenderingTests`. The waiter/kitchen half runs inside the test
through the typed API clients as `waiter1` / `kitchen1`. The billing terminal's invoice printer points at
`127.0.0.1:9100` (nothing listens) so the invoice print **fails for real**; no receipt printer is set up.

Regenerate (API running; build to an OutDir inside the repo so a running desktop app does not lock `bin`):

```powershell
$env:HOTELPOS_LIVE_SCREENS = "1"                      # gate; the test is skipped otherwise
$env:HOTELPOS_LIVE_API = "http://localhost:5100"      # default http://localhost:5000
$env:HOTELPOS_LIVE_SCREENS_ROLES = "cashier"          # cashier | admin (falls back to manager1) | functions*
$env:HOTELPOS_ADMIN_PASSWORD = "<admin pw>"           # admin is only tried when set
dotnet test tests/HotelPOS.Desktop.Tests -c Debug -p:OutDir=C:\a-personal\Project\HotelPOS\Restaurant_Pos\tests\HotelPOS.Desktop.Tests\bin\p21live\ --filter "FullyQualifiedName~LiveFlowScreenshots"
```

Device names must already be registered: `BILLING-01`, `ADMIN-01`, `VERIFY-01` (waiter), `APP-KITCHEN-B4134F`
(kitchen); override with `HOTELPOS_LIVE_DEVICE_BILLING|WAITER|KITCHEN|ADMIN`. The cashier flow uses the table in
`HOTELPOS_LIVE_TABLE` (default T02). If WPF stops with `BG1002 ... baml`, delete `src/HotelPOS.Desktop/obj` and retry.
A capture log is written to `%TEMP%\hotelpos-live-screens.log`.

**This run:** roles `cashier` and `admin`. `admin` with the guessed password `Admin@123` failed ("Invalid username or
password"; tried once, nothing changed), so the management screens were captured as **manager1** and the admin-only
screens (Users, Roles, Devices, Settings, Licence, Server) were not regenerated. The `functions-cashier`, `functions-cashier-reopen` and `functions-manager` passes were re-run against the workspace.

## `cashier/` — the cashier flow (cashier1 on BILLING-01), 19 screens

| File | What it shows |
|---|---|
| `01-cashier-sign-in.png` | Sign-in with the live "Server online" check, terminal `BILLING-01 · Billing`. |
| `02-cashier-home.png` | Cashier home (Overview group) after the waiter asked for T02's bill: pending bills, No shift, Expected cash, Print failures, Business day. |
| `03-cashier-open-shift.png` | Open-shift panel with a counted float of 3,000.00 (submitted afterwards). |
| `04-cashier-workspace-queue.png` | **Cashier workspace**, nothing selected: Bills pane with search, chips All / Waiting 3 / Invoiced / Partly paid / Paid, sort Oldest first / Highest amount, three bill cards (table, order, Waiting badge, waiter, minutes, total); the centre and payment panes empty; shift bar above. |
| `05-cashier-workspace-bill.png` | T02 selected (claimed): order column with initials thumbnails, options "Spicy, Extra raita (+30.00 each)", the "Less oil" note badge, real CGST/SGST rows, round-off, total 1,019.00, Customer..., Finalize & print / Reopen / Void; payment column with the Amount due hero, Cash F2 (selected), Card F3, UPI F4, Split payment F9, Amount to pay, quick amounts, Complete payment and the hotkey hint. |
| `05-cashier-workspace-collapsed.png` | The same at 1366 px with the sidebar collapsed to the icon rail (C5). |
| `06-cashier-discount.png` | Discount panel, manual 5 % "Regular guest" (inside the 10 % cashier limit; applied without approval). |
| `07-cashier-cash-payment.png` | After Finalize & print (INV-202610-000013; auto-print failed, "1 print failed" in the status bar): F2 cash with 1,500.00 received and the change shown. |
| `07-cashier-split-payment.png` | Split payment cash 480 + card 488 with a card reference, Remaining 0.00. |
| `08-cashier-paid.png` | **Inline Paid state**: Paid pill, invoice number, change 0.00, the two tenders, Print receipt / Print invoice / Next bill; the queue down to 2 bills; toast "Paid. Table T02 is free." |
| `09-cashier-print-receipt-warning.png` | Print receipt with no receipt printer: the warning toast. |
| `09-cashier-queue-paid-chip.png` | The **Paid** chip: today's settled bills in the queue pane, read-only. |
| `10-cashier-print-log.png` | Print Log on the Failed filter: the invoice copy, printer 127.0.0.1:9100, the reason, Retry. |
| `11-cashier-closed-bills.png` | Closed bills for today with the T02 invoice selected. |
| `12-cashier-pay-out.png` | Pay in / pay out panel: Pay-out 450 "Vegetables from the market" (recorded). |
| `13-cashier-x-report.png` | X report: float, cash sales, pay-outs, expected cash, payments on this terminal, movements. |
| `14-cashier-close-shift.png` | Close shift with a count 50.00 short and the reason (closed afterwards). |
| `15-cashier-notifications-bell.png` | Notices panel with the real events of the session. |
| `16-cashier-notification-preferences.png` | Notifications... dialog (Pop-up / Sound per kind). |

## `manager/` — the management screens as manager1 on ADMIN-01, 16 screens (+ 2 Phase 22 renders)

| File | What it shows |
|---|---|
| `17-manager-dashboard.png` | **Dashboard**: navy grouped sidebar (Overview, Live Operations, Menu Management ...), "Business day Sat 17 Oct", Online pill, KPI cards Today's sales 968.00 (down 82.9 % vs 5,666.00 yesterday), Orders today 1 (4 open now), Average order value, Occupied tables 4 / 16 with the occupancy bar; Live Order Status donut (4 active: Sent to kitchen, Served, Bill requested); Active Orders with search, chips, Newest first, ten per page, View. |
| `17-manager-dashboard-panels.png` | Scrolled to the end: Table status cards with View floor plan, Recent activity (audit-log sentences, View all), 7-day bars, Payments today donut, Top items, Kitchen performance, Staff today and the Terminals placeholder (manager). |
| `17-manager-dashboard-collapsed.png` | The dashboard at 1366 px with the icon-rail sidebar (A8). |
| `18-manager-orders.png` | **Orders** page: search, "5 active orders", Newest first, chips All (5) / Sent to kitchen (1) / Served (1) / Bill requested (3), grid #, TABLE, WAITER, ITEMS, TOTAL, STATUS badge, CREATED, ELAPSED, View (after the column fix below). |
| `18-manager-tables-sections.png` | Sections & Tables (Live Operations), Tables tab with T02 in the editor. |
| `19-manager-menu-categories.png` | **Menu, Categories tab** (deep link by title): ORDER, NAME, ACTIVE ITEMS, STATUS, New category. Tabs Categories, Items, Modifiers, Preview (no Taxes / Stations for a manager). |
| `19-manager-menu-items.png` | Menu, Items tab. |
| `20-manager-menu-item-editor.png`, `20-manager-menu-item-editor-modifiers.png` | Edit item "Chicken Biryani", and the end of the form with the modifier groups. |
| `24-manager-audit-log.png` | Audit Log (System group) with the first entry selected. |
| `25-manager-reports-daily-sales.png` | Reports hub (Analytics & Reports), Daily sales for today. |
| `26-manager-gst-hub.png` | Tax Returns (Finance), current month. |
| `27-manager-day-end-checks.png` | Day End step 1 with the real blocking items. Not run. |
| `29-manager-printers.png` | Printers (Devices group) of ADMIN-01. |
| `31-manager-command-palette.png` | Command palette (Ctrl+K) with the query "re". |
| `32-manager-dashboard-dark.png` | The dashboard in the dark theme (same tokens, same status colours). |
| `58-manager-dashboard-phase22.png` | **Phase 22** dashboard (rendering harness, demo data, 19 Oct 2026): five KPI cards laid out 3 + 2 at 1366 px — the fifth is **Expenses today** with "… after expenses" — and the Hourly sales / Staff today panels further down. |
| `59-manager-expenses.png` | **Phase 22** Finance → Expenses (rendering harness): date range and category filters, Record expense, void with reason; categories are edited under Settings. |

## `admin/` — 7 admin-only screens, **pre-Phase 21** (16 Oct 2026), plus 2 Phase 22 screens (19 Oct 2026)

Phase 22 (rendering harness, demo data): `56-admin-floor-plan` — the Admin **Floor plan** editor with section picker,
snap-to-grid, Rotate 90°, Round / Rectangle, Undo / Redo, Reset to grid, zoom + Fit, selection handles on T02 and the
Unplaced tables tray; `57-admin-table-map-floor-plan` — the Tables map in **Map** mode (List / Map toggle, same tiles
with live status, reservation mark) as every desktop role sees it.

`21-admin-users`, `22-admin-roles-permissions`, `23-admin-devices`, `28-admin-settings`, `28-admin-settings-notifications`,
`30-admin-licence`, `30-admin-server`: kept because they are the only pictures of those screens, but they show the old
shell; regenerate them with `HOTELPOS_ADMIN_PASSWORD` set. The admin pictures of screens a manager also has (dashboard,
tables, menu, audit, reports, GST, day end, printers, palette, dark) were deleted; `manager/` has the Phase 21 ones.

## `functions/` — function-by-function pass (16–17 Oct 2026; bill functions 41–43 and 46 re-shot 17 Oct against the cashier workspace), 82 screens

`NN-area-action-before.png` / `-dialog[N].png` / `-after.png` for: users create / edit / reset password / deactivate /
unlock (01–05), roles edit and revert (06), sections add (07), tables add / rename / deactivate (08–10), category and
item create, sold out, back in stock (11–14), palette table search (15), settings kitchen-warn edit and revert (16–17),
printers test print (18), licence / server / backup pages (19–21), audit filter (22), day-end precheck (23), GST current
month (24), the eight reports and their Excel / PDF exports and dark theme (25–28), cancel an order line as manager
(30–31), customer create (40), refund of a card payment with manager approval (44) and Closed Bills dark (45). Thebill functions `41–43` and `46` (attach customer, over-limit discount with manager approval, pay by card, reopen aninvoiced bill) are captured on the **cashier workspace** (select bill, then the composed bill-detail commands; 41 and 43before/after, 42 before/dialog/after, 46 before/dialog/dialog2/after). The other functions are the 16 Oct captures except30/31 and 40/44/45 (re-shot 17 Oct). Replay: `HOTELPOS_LIVE_SCREENS_ROLES=functions-manager,functions-cashier,functions-cashier-reopen` (the manager part prepares the T02 bill).

## Comparison with the mock-ups (`docs/design/reference/cashier-workspace.png`, `admin-dashboard.png`)

| Screen | Matches | Differs | Gap by design (plan § 3 Out / § 4) |
|---|---|---|---|
| Cashier workspace (`cashier/05`) | Three panes with the mock-up's proportions; navy grouped sidebar; bill cards with badge, waiter, wait, total; order column with thumbnails, options and notes, totals; payment column with due hero, method buttons, quick amounts, Amount received / Change, green Complete payment; the mock-up's Quick actions become the Finalize / Reopen / Void row | Thumbnails are initials (no pictures uploaded yet, D11); "Not invoiced yet" and real GST rows instead of "Tax (10 %)"; the sidebar has the HotelPOS groups, not the mock-up's Expenses / Customers / Sales Report items | No Dine In / Takeaway / Delivery chips (no order types); no Edit order / Add items / Send to kitchen / Cancel order for the cashier (waiter flow); no Wallet method unless configured (D6); Customers hidden (D3; reached by the "Manage customers…" link in the bill customer picker) |
| Cashier paid (`cashier/08`) | Inline Paid state in the payment column, tenders listed, Print receipt / invoice / Next bill | The mock-up has no paid state; ours keeps the order visible and frees the table (toast) | — |
| Paid chip (`cashier/09-cashier-queue-paid-chip`) | Chip row Waiting / Invoiced / Partly paid / Paid like the mock-up's Open / Partial / Ready / Paid | Status words follow the shared status map | — |
| Dashboard light (`manager/17`) | Header with business day and Online pill; four KPI cards with delta and occupancy bar; Live Order Status donut with legend; Active Orders table with chips, View, pager, View all; Table status cards; Recent activity; Top items; Staff and Terminals | KPI icons are outline glyphs, not filled tiles; the table has no Customer column (bills carry the customer, orders do not); only statuses present appear in the legend | No Low stock card or table, Cash reconciliation, date-range or branch pickers, sparklines (§ 3 Out, A2); Terminals placeholder for managers (`api/admin/devices` is admin-only). Phase 22 added the Hourly sales bars (F1) and a fifth KPI card, Expenses today, that opens Reports → Profit & Loss (F4) |
| Dashboard dark (`manager/32`) | Same layout; status colours and badges keep their meaning; AA contrast per the token tests | The mock-up has no dark variant | — |
| Dashboard collapsed (`manager/17-manager-dashboard-collapsed`) | Icon rail like the mock-up's hamburger state; the content takes the width | No group labels on the rail (icons only; tooltips carry the names) | — |
| Orders (`manager/18-manager-orders`) | The mock-up's Active Orders list as a page: chips, search, sort, status badges, View | Columns CREATED and ELAPSED instead of one Time column; no row "..." menu | — |
| Sections & Tables (`manager/18-manager-tables-sections`) | Grid with status words, editor on the right, New table | The mock-up draws a floor plan ("Tables & Floor Plan") | The floor plan is a separate Admin screen, **Floor plan** (Phase 22 P2), and the Map view of the table map |
| Menu tabs (`manager/19`) | Categories and Items as the two Menu Management entries of the mock-up, here tabs of one screen (D7) | — | Modifier management stays as it was (D2 bans new modifier UI) |

## UI/UX issues found in this pass (ordered by severity)

1. **Fixed (medium) — Right-hand grid columns clipped at 1366 px.** `manager/18-manager-orders.png` showed "Viev",
   "CREATEC", "20 h 39 r" and the dashboard's Active Orders "ITEM≤" / "Viev": the fixed column widths were narrower
   than the padded content. Widened CREATED 80→96, ELAPSED 96→112, View 64→80 in
   `src/HotelPOS.Desktop/Modules/Orders/ActiveOrdersView.xaml` and ITEMS 62→72, ELAPSED 82→100, View 66→80 in
   `src/HotelPOS.Desktop/Modules/Dashboard/DashboardView.xaml`; the manager run was repeated and `ScreenRenderingTests`
   is green.
2. **Fixed (medium) — Payment column: the Change box needed a scroll at 800 px.** `Themes/Cashier.xaml` payment template: method toggles in three columns, quick amounts in one row of four (44 px, smaller padding so "2,000.00" is whole), the "Quick amounts" caption dropped, tighter margins and footer padding; due hero, methods, split, amount to pay, quick amounts, received, Change due and Complete payment now all fit in the 1366×800 window with no scrollbar (`cashier/07`).
3. **Fixed (medium) — Sidebar (`ShellView.xaml`: scrollbar Auto with a thin themed thumb; footer pinned; not re-shot, cashier nav is short) navigation is cut at "Menu Management" at 800 px.** Every manager screen: the nav list
   ends under the device card with a half-visible group label and no visible scrollbar, so Finance, Analytics,
   Devices and System are reachable only by wheel-scrolling or the palette (`src/HotelPOS.Desktop/Shell/ShellView.xaml:250`,
   the nav `ScrollViewer`; check whether its bar is styled invisible or the row grows past the window).
4. **Fixed (low, `BillingQueueViewModel.Tick`: "26 h 51 min", "2 d 2 h") — Wait time on bill cards is in raw minutes.** `cashier/04`: "1593 min", "1507 min" for the stale demo
   bills, while the Orders page says "26 h 41 min" (`src/HotelPOS.Desktop/Modules/Billing/CashierWorkspaceView.xaml`, card template).
5. **Fixed (low, toast host margin top 124 in `MainWindow.xaml`) — Toasts cover the shift bar text.** `cashier/08`: the top-centre toast hides "Shift open since 17:21 ·
   Priya (Cashier) · expected ..." (the buttons stay clear).
6. **Fixed (low, hidden when IsPaid in `Themes/Cashier.xaml` and `BillDetailView.xaml`) — "Add discount" still rendered beside an applied discount on a paid bill.** `cashier/08`: the greyed
   link reads oddly next to "Discount 5% · Regular guest"; hide it once the bill is settled
   (`src/HotelPOS.Desktop/Modules/Billing/BillDetailView.xaml`, totals block).
7. **Fixed (low, "Manage customers…" link, `ManageCustomersCommand`) — D3's "Manage customers" link is missing.** The bill's customer picker says "add the customer on the
   Customers screen" (`src/HotelPOS.Desktop/Modules/Billing/BillDetailView.xaml:308`) but Customers is no longer in the
   sidebar; only the command palette reaches it.
8. **Info — not regenerated.** Admin-only screens (password unknown); the `functions/` pass was rewritten for the workspace (see its section)
   rewrite); the Terminals panel is still a placeholder for managers (Phase 20 issue 11, by design of the API).

Binding errors: the live test installs no `PresentationTraceSources` listener; `ScreenRenderingTests` (which fails on
any binding error, both themes) passed after the fix, so the rendered screens report **0**.

Phase 20 carry-overs still open after this pass (numbers from the 16 Oct list, kept in git history): 11 Terminals tile
for managers, 12 audit preset "Today" vs month range, 18 lower-cased GST acronyms, 21 item editor squeezes the list,
22 same filter two labels, 23 Printers page, 25 notification preferences list cut mid-row, 26 palette groups not
visible, 29 refund is three dialogs, 34 reset-password dialog, 36 audit action filter exact match. Issue 14 ("Discount
DISCOUNT") is gone with the new totals block.

What worked well: the three panes make claim, discount, finalize and pay one uninterrupted path with no page change; the
hotkeys are printed on the buttons and repeated in the hint line; the Paid state keeps the bill on screen for the
receipt; the dashboard's chips and pager mirror the Orders page exactly; the dark theme is complete on every screen.

## `admin/` — Phase 25 renders (23 Oct 2026, `ScreenRenderingTests` output, fake data, 1366 × 800)

Files `60`–`67` with the `-phase25` suffix (light, and `-dark` where the theme matters): Users (elevated editor, badges,
quiet action row), Devices (badges, empty state), Printers (primary action in the header), Expenses (wrapping filter
bar, badges), GST returns (capital headers), Cash shifts (badges, variance colour), Backup (badges), Audit Log (column
widths). All 167 renders pass in both themes with zero binding errors. What changed and what was deliberately left:
`docs/phases/phase-25-admin-ui-polish.md`.
