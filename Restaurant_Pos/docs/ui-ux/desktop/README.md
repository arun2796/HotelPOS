# Desktop client: screenshots of the real cashier, manager and admin flows

Captured on 16 Oct 2026 from the **real WPF client against the running Development API** (`http://localhost:5000`,
seeded demo data), 1366×800, light theme unless the name says `dark`. Nothing here is sample data: every screen is
the real view bound to the real view-model, fed by the real `ApiClient`, `RealtimeClient` and session. Only the
machine-bound services are replaced (settings and the secure store live in memory; file picker, app lifetime and sound
player are stubs; the file picker returns `%TEMP%\hotelpos-live-exports\<suggested name>` for exports).

How: the opt-in xUnit test `tests/HotelPOS.Desktop.Tests/Rendering/LiveFlowScreenshots.cs` builds the desktop
composition root (`AppHost.ConfigureServices`), signs in through the real login screen, drives the flow through the
view-models and renders each screen with the same `RenderTargetBitmap` method as `ScreenRenderingTests`. Dialogs that
open on the way (reason prompts, confirmations, manager approvals) are answered by the test and captured as `-dialog`.
The waiter/kitchen half (order on T02, kitchen accept/start/ready, serve, request bill) runs inside the test through
the typed API clients as `waiter1` / `kitchen1`. The billing terminal's invoice printer points at `127.0.0.1:9100`
(nothing listens) so the invoice print **fails for real**; no receipt printer is set up, so PRINT RECEIPT shows the
real warning toast.

Regenerate (API running; the owner's desktop app may keep running: build to an OutDir inside the repo):

```powershell
$env:HOTELPOS_LIVE_SCREENS = "1"             # gate; the test is skipped otherwise
$env:HOTELPOS_ADMIN_PASSWORD = "<admin pw>"   # admin is only tried when set
$env:HOTELPOS_LIVE_SCREENS_ROLES = "cashier"  # cashier | admin | functions | functions-admin-edits |
                                              # functions-admin-views | functions-manager | functions-cashier | functions-cashier-reopen
dotnet test tests/HotelPOS.Desktop.Tests -c Debug -p:OutDir=C:\a-personal\Project\HotelPOS\Restaurant_Pos\tests\HotelPOS.Desktop.Tests\bin\live\ --filter "FullyQualifiedName~LiveFlowScreenshots"
```

Device names must already be registered (the licence counts terminals): defaults `BILLING-01`, `ADMIN-01`,
`VERIFY-01` (waiter), `APP-KITCHEN-B4134F` (kitchen); override with `HOTELPOS_LIVE_DEVICE_BILLING|WAITER|KITCHEN|ADMIN`.
`cashier`, `functions-manager` and `functions-cashier*` create orders and bills on **T02**; `functions-admin-edits`
creates user `waiter9`, section `Terrace`, table `X01`, category `Chef specials` and item `Kothu Parotta`, so it is not
replayable as-is (the saves would report duplicates). A capture log is written to `%TEMP%\hotelpos-live-screens.log`.

## `cashier/` — the cashier flow (cashier1 on BILLING-01), 16 screens

| File | What it shows |
|---|---|
| `01-cashier-sign-in.png` | Sign-in screen with the live "Server online" check, terminal `BILLING-01 · Billing`. |
| `02-cashier-home.png` | Cashier home after the waiter asked for T02's bill: 2 pending bills (T05 from the browser agent, T02 "just now"), No shift, Expected cash —, Print failures 0, Business day Open. |
| `03-cashier-open-shift.png` | Open shift dialog with a counted float of 3,000.00. Submitted for real afterwards. |
| `04-cashier-billing-queue.png` | Billing queue with the shift bar ("Shift open since 14:53 · expected 3,000.00") and the T02 card (Order #1006, 1,019.00). |
| `05-cashier-bill-detail.png` | T02 bill claimed and open: 3 lines (Chicken Biryani ×2 with extras, Paneer Tikka, Butter Naan ×3), GST rows, round-off, total 1,019.00; the "bill claimed by BILLING-01" toast. |
| `06-cashier-discount.png` | Discount panel, manual 5 % "Regular guest" (inside the 10 % cashier limit; applied without approval). |
| `07-cashier-split-payment.png` | After FINALIZE & PRINT (invoice INV-202610-000004, auto-print failed → "1 print failed" in the status bar): split payment cash + card with a card reference, Remaining 0.00. |
| `08-cashier-paid.png` | PAID dialog with the two tenders, change 0.00, PRINT RECEIPT / PRINT INVOICE / NEXT BILL, plus the two "paid" toasts. |
| `09-cashier-print-receipt-warning.png` | PRINT RECEIPT with no receipt printer: the warning toast "No receipt printer is set up on this terminal (Printers in the menu)". |
| `10-cashier-print-log.png` | Print Log on the Failed filter: the invoice copy, printer 127.0.0.1:9100, the reason and Retry. |
| `11-cashier-closed-bills.png` | Closed bills for today with the T02 invoice selected: lines, print history "(failed)", the two payments with Reprint / Refund…. |
| `12-cashier-pay-out.png` | Pay in / pay out panel: Pay-out 450 "Vegetables from the market" (recorded for real). |
| `13-cashier-x-report.png` | X report: float 3,000, cash sales 210, pay-outs 450, expected 2,760, payments on this terminal, cash movements. |
| `14-cashier-close-shift.png` | Close shift with a count 50.00 short of the expected 2,760.00 and the reason; the pay-out toast overlapping the CLOSE SHIFT button. Closed for real afterwards. |
| `15-cashier-notifications-bell.png` | Notices panel with the real events of the session: bill asked, bill claimed, invoice did not print (Critical), table paid. |
| `16-cashier-notification-preferences.png` | Notifications… dialog (Pop-up / Sound per kind, saved on this terminal). |

## `manager/` — the management screens as manager1 on ADMIN-01, 13 screens

Captured during the first pass when the admin password was unavailable; kept because a manager sees a different
dashboard (no terminals) and no Administration group.

| File | What it shows |
|---|---|
| `17-manager-dashboard.png` | Dashboard, top of the page: 6 KPI tiles, net sales 7 days, payments donut, top items. |
| `17-manager-dashboard-panels.png` | Dashboard scrolled to the end: Kitchen performance today, Staff today, Terminals (manager placeholder). |
| `18-manager-tables-sections.png` | Sections & Tables, Tables tab with T02 selected in the editor. |
| `19-manager-menu-items.png` | Menu, Items tab (no Taxes / Stations tabs for a manager). |
| `20-manager-menu-item-editor.png` | Edit item "Chicken Biryani". |
| `20-manager-menu-item-editor-modifiers.png` | The same editor scrolled to the end: HSN/SAC, sort order, Available / Active and the modifier groups. |
| `24-manager-audit-log.png` | Audit log with the day's entries and the Auth.LoginSucceeded detail. |
| `25-manager-reports-daily-sales.png` | Reports hub, Daily sales for today. |
| `26-manager-gst-hub.png` | Tax Returns (GST hub), GSTR-1 B2B for September 2026: empty with the validation banner. |
| `27-manager-day-end-checks.png` | Day End step 1 with the real blocking items. Not run. |
| `29-manager-printers.png` | Printers of terminal ADMIN-01 (nothing configured). |
| `31-manager-command-palette.png` | Command palette (Ctrl+K) with the query "re". |
| `32-manager-dashboard-dark.png` | The dashboard in the dark theme. |

## `admin/` — the same screens as admin on ADMIN-01 plus the Admin-only ones, 20 screens

| File | What it shows |
|---|---|
| `admin/17-admin-dashboard.png`, `admin/17-admin-dashboard-panels.png` | Admin dashboard (top, and scrolled to the Kitchen performance / Staff / Terminals panels; for an admin the Terminals panel lists the devices). |
| `18-admin-tables-sections.png` | Sections & Tables with the table editor open. |
| `19-admin-menu-items.png`, `20-admin-menu-item-editor.png`, `20-admin-menu-item-editor-modifiers.png` | Menu items (with the Taxes and Stations tabs an admin gets), the item editor and its modifier groups. |
| `21-admin-users.png` | Users with cashier1 open in the editor (roles as checkboxes). |
| `22-admin-roles-permissions.png` | Roles & Permissions matrix, Cashier selected (locked core permissions, "Force re-login"). |
| `23-admin-devices.png` | Terminal & Device Management: live/offline, type, station, machine, version, status, last seen. |
| `24-admin-audit-log.png` | Audit log with the first entry selected. |
| `25-admin-reports-daily-sales.png` | Reports hub, Daily sales. |
| `26-admin-gst-hub.png` | GST hub (September 2026 default). |
| `27-admin-day-end-checks.png` | Day End precheck. Not run. |
| `28-admin-settings.png`, `28-admin-settings-notifications.png` | Restaurant Settings (top of the list; and scrolled to the end where the Notifications section sits). |
| `29-admin-printers.png` | Printers of ADMIN-01. |
| `30-admin-licence.png` | Licence: Licensed, Standard, 5 registered of 25, machine id, apply a key. |
| `30-admin-server.png` | Server: notice composer and the live server log. |
| `31-admin-command-palette.png` | Command palette with "re". |
| `admin/32-admin-dashboard-dark.png` | Dashboard in the dark theme. |

## `functions/` — function-by-function pass, before / after each action, 83 screens

Each row is one function: `NN-area-action-before.png` (state and filled-in form before the action),
`-dialog[N].png` (every dialog the action opened, with the answer typed in) and `-after.png` (the result). All actions
ran against the API; the records they created are still in the demo database (listed above).

| Role | Function | Files | Result |
|---|---|---|---|
| admin | Users: create `waiter9` (Kavya, Waiter) | `01-users-create-*` | Created; selected in the list with the success toast. |
| admin | Users: edit the display name | `02-users-edit-*` | "Kavya R (Waiter)" saved. |
| admin | Users: reset password | `03-users-reset-password-*` (dialog: temporary password) | Reset; must change at next sign-in. |
| admin | Users: deactivate (then reactivated quietly) | `04-users-deactivate-*` (dialog: confirm) | Shown with "Show inactive" ticked. |
| admin | Users: unlock after 5 wrong passwords | `05-users-unlock-*` | Locked by the API (ACCOUNT_LOCKED), unlocked from the screen; the "before" still shows LOCKOUT "Normal" (see issue 33). |
| admin | Roles: grant "Configure sections and tables" to Cashier, save (then reverted) | `06-roles-edit-*`, `06-roles-revert-dialog` (dialog: Save permissions) | Saved without forcing re-login. |
| admin | Sections: add "Terrace" | `07-sections-add-*` | Created. |
| admin | Tables: add X01 "Terrace 1" (2 seats) in Terrace | `08-tables-add-*` | Created. |
| admin | Tables: rename X01 to "Terrace window" | `09-tables-rename-*` | Updated. |
| admin | Tables: deactivate X01 | `10-tables-deactivate-*` | Updated; listed only with "Show inactive". |
| admin | Menu: create category "Chef specials" | `11-menu-category-create-*` | Created. |
| admin | Menu: create item "Kothu Parotta" (C01, 180, GST 5 %, Main Kitchen, Spice level) | `12-menu-item-create-*` (before is scrolled to the modifier groups) | Created; the editor stays open for a picture. |
| admin | Menu: mark sold out, then back in stock | `13-menu-sold-out-*`, `14-menu-back-in-stock-*` | Toasts "is sold out" / "is available"; button flips Mark sold out / Mark available. |
| admin | Command palette: table search "t02" | `15-palette-menu-table-search` | Finds the table and the screens. |
| admin | Settings: Kitchen warn minutes 10 → 15, save, revert | `16-settings-kitchen-warn-*`, `17-settings-revert-after` | Saved twice; back to 10. |
| admin | Printers: test print of the Invoice profile to 127.0.0.1:9100 (not saved) | `18-printers-test-print-*` | "Printer 127.0.0.1:9100 did not answer…" under the button. |
| admin | Licence, Server, Backup pages | `19-licence-page`, `20-server-page`, `21-backup-page` | Views only (no backup run). |
| admin | Audit log: filter user cashier1 + action "Bill." | `22-audit-filter-*` | 0 records (see issue 36). |
| admin | Day End precheck | `23-dayend-precheck` | Step 1 with the blocking items; not run. |
| admin | GST hub for October 2026 | `24-gst-current-month-*` | Footer: 4 invoices, value 2,615.00; B2B section empty; validation banner. |
| admin | Reports: daily, items, categories, waiters, kitchen, cancellations, discounts, taxes | `25-reports-01…08-*` | Each hub for today. |
| admin | Reports: Excel export (daily sales), PDF (item sales) | `26-reports-export-excel-*`, `27-reports-save-pdf-*` | `%TEMP%\hotelpos-live-exports\sales-daily-2026-10-16-to-2026-10-16.xlsx`, `sales-items-2026-10-16-to-2026-10-16.pdf`. |
| admin | Dark theme on Reports | `28-reports-dark` | |
| manager | Tables: cancel an order line (2 × Chicken Biryani) on T02 order #1007 | `30-order-cancel-line-*` (dialog: reason) | Line struck through, total 970 → 390. A manager needs no approval (the API rule is ticket status + role), so no approval dialog appeared. |
| manager | Dark theme on Tables | `31-tables-dark` | |
| cashier | Customers: create "Ravi Kumar" | `40-customers-create-*` | Created. |
| cashier | Bill: attach the customer (picker search "Ravi") | `41-bill-attach-customer-*` | Customer line shows Ravi Kumar. |
| cashier | Bill: 25 % discount above the 10 % cashier limit | `42-bill-discount-approval-*` (dialog: Manager approval manager1) | Applied: "Discount 25% · Birthday party", 410 → 307. |
| cashier | Bill: pay by card | `43-bill-pay-card-*` | PAID, INV-202610-000005. |
| cashier | Closed bills: refund 100.00 of the card payment | `44-refund-partial-*` (dialogs: amount, reason, Manager approval) | "Paid · 100.00 back", refund row −100.00. |
| cashier | Dark theme on Closed Bills | `45-closed-bills-dark` | |
| cashier | Bill: finalize order #1008 (invoice issued) and REOPEN it | `46-bill-reopen-invoiced-*` (dialogs: reason, Manager approval; the first run's `-dialog3` is the retry prompt after the approver was rate-limited) | Invoice cancelled, order back with the waiter. A **settled** bill cannot be reopened by design (REOPEN is offered only while nothing is paid; settled bills are refunded), so the invoiced-unpaid case was used. Order #1008 was settled through the API afterwards to free T02. |

## UI/UX issues

Ordered roughly by how much they would bother a cashier or manager at the counter. 1–26 from the flow pass, 27 onwards
from the function pass.

1. **Toasts cover modal and page buttons.** `cashier/14-cashier-close-shift.png`: "Pay-out of 450.00 recorded." sits
   on CLOSE SHIFT; `cashier/08-cashier-paid.png`: two toasts cover NEXT BILL; `functions/06-roles-edit-before.png`:
   the Users toast ("waiter9 has been unlocked") survives navigation and hides Save permissions;
   `functions/12-menu-item-create-after.png`: hides Close / Save changes of the editor. Toast host in
   `Shell/ShellView.xaml` (bottom-right `ItemsControl`) is above the dialog layer and the page's action row.
2. **The terminal is notified about its own actions.** `cashier/05-cashier-bill-detail.png`, `06`, `15`: "Table T02
   bill claimed by BILLING-01" with an **Open** button appears on the very terminal that just claimed it, and
   `cashier/08-cashier-paid.png` shows both "Table T02 paid (INV-…, 439.00) · Open" and "Paid. Table T02 is free." for
   one payment. `Contracts/Realtime/NotificationKind.cs` (`BillClaimed`, `PaymentCompleted`),
   `Services/Ui/NotificationRouter.cs` (no originating-device filter), `BillDetailViewModel.ConfirmPaymentAsync`.
3. **Bill lines hide the chosen options and notes.** `cashier/05-cashier-bill-detail.png`: Chicken Biryani (Spicy,
   Extra raita) shows only "incl. extras 30.00 each"; Paneer Tikka (Medium, "Less oil") shows nothing, while the
   table panel (`functions/30-order-cancel-line-before.png`) does show them. `BillDetailViewModel.Lines`
   (`Modules/Billing/BillDetailViewModel.cs` ~l.329), `BillDetailView.xaml`.
4. **Print-failure notices use the internal bill id.** `cashier/15-cashier-notifications-bell.png`: "Invoice #5 did
   not print" while the screen and the print log say INV-202610-000004. `NotificationKind.PrintFailed`
   (`Contracts/Realtime/NotificationKind.cs` l.157).
5. **Misleading help in the no-printer toast for cashiers.** `cashier/09-cashier-print-receipt-warning.png`:
   "(Printers in the menu)" — a Cashier has no Printers entry (`Services/Navigation/ModuleRegistry.cs`); text in
   `Services/Printing/DocumentPrinter.cs` l.70.
6. **Print Log clips the two columns that matter.** `cashier/10-cashier-print-log.png`: DOCUMENT and WHY are cut
   without ellipsis or tooltip while COPY and STATUS have spare width. `Modules/Printing/PrintLogView.xaml` l.39-44.
7. **Denomination sheet does not fit the dialog.** `cashier/03-cashier-open-shift.png`, `14`: the notes column is cut
   mid-row (₹20 half visible, ₹10 hidden) and scrolls inside a modal while the coins column has empty space.
   `Modules/Cash/DenominationSheetView.xaml`, `ShiftCountPanelView.xaml`.
8. **Dashboard panels are below the fold.** `admin/17-admin-dashboard.png`, `manager/17-…`: at 1366×800 the Kitchen
   performance, Staff and Terminals panels are only visible after scrolling. `Modules/Dashboard/DashboardView.xaml`.
9. **Inconsistent currency formatting on the dashboard.** `admin/17-admin-dashboard.png`: the payments legend shows
   "₹1,589" / "₹690" next to "2,279.00" everywhere else. `Modules/Dashboard/DashboardViewModel.cs` l.288 hard-codes
   `"₹" + N0`; the rest uses `Money.Format`.
10. **Copy says "Today in blue" but today's bar is green**, and empty days show a "0" label each.
    `admin/17-admin-dashboard.png`, `…-dark.png`; `DashboardView.xaml` net-sales card.
11. **Terminals tile is dead for managers.** `manager/17-manager-dashboard-panels.png`: a placeholder "Terminals are
    listed for administrators" with an "Open audit log" button takes a third of the row.
12. **Audit log clips and mislabels.** `admin/24-admin-audit-log.png`: TIME "16 Oct 26, 14:5" (Width=120) and ACTION
    "Auth.LoginSucceec" are cut; the detail header reads "By: manager1  on:" with nothing after "on:" when the entry
    has no terminal (`AuditLogsView.xaml` l.105-106); the preset says "Today" while the range beside it reads
    16-09-2026 to 16-10-2026; the placeholder "Action, e.g. Order.C…" is clipped at Width=150.
13. **Three names for the cashier's home.** `cashier/02-cashier-home.png`: sidebar "Dashboard", top bar "Dashboard",
    page heading "Cashier home" (`ModuleRegistry.cs`, `Modules/Cash/CashierHomeView.xaml`); the My shift tile says
    "open one from the bar under the top bar".
14. **"Discount DISCOUNT".** `cashier/05-cashier-bill-detail.png`: the totals row label and the action link are the
    same word; the totals card leaves a large empty area under TOTAL (`Modules/Billing/BillDetailView.xaml`).
15. **Sidebar user block truncates name and role.** Every screen: "Priya…" / "Cashier", "Meen…" / "Mana…",
    "Admi…" / "Admin". `Shell/ShellView.xaml` l.82-84.
16. **Page descriptions are cut in the top bar.** `admin/26-admin-gst-hub.png`, `27`, `cashier/10`,
    `functions/19-licence-page.png` ("…where to apply a…"). Single-line `PageDescription` in `Shell/ShellView.xaml`.
17. **Closed bills: unlabeled filters.** `cashier/11-cashier-closed-bills.png`: an empty date box with a "Today"
    button and an empty search box with no placeholder; the print history line "Original 14:50 BILLING-01 (failed)"
    does not say what failed. `Modules/Billing/ClosedBillsView.xaml`.
18. **GST hub opens on an error.** `admin/26-admin-gst-hub.png`: default month is the previous month and the red
    "1 error(s) must be fixed…" banner sits above an empty "Nothing to show" grid; headers "Customer gstin", "Igst",
    "Cgst", "Sgst" lower-case the acronyms. `Modules/Gst/GstHubView.xaml`, `GstHubViewModel.cs`.
19. **Plurals.** `admin/27-admin-day-end-checks.png`: "1 unpaid bills", "1 open cash shifts"; Reports: "1 rows".
    `Modules/Cash/DayEndViewModel.cs`, `Modules/Reports/ReportsViewModel.cs`.
20. **Unstyled checkbox column.** `admin/19-admin-menu-items.png`: the IN STOCK `DataGridCheckBoxColumn` renders the
    default WPF checkbox unlike the themed checkboxes beside it. `Modules/MenuAdmin/MenuItemsPageView.xaml` l.83.
21. **Opening the item editor squeezes the list.** `admin/20-admin-menu-item-editor.png`: STATION is clipped and the
    search box shrinks; the modifier groups are the last thing in a scrolling form. `MenuItemsPageView.xaml`.
22. **Same filter, two labels.** "Show inactive" on Sections & Tables and Users vs "Inactive" on Menu.
    `Modules/Admin/TablesView.xaml`, `UsersView.xaml`, `Modules/MenuAdmin/MenuItemsPageView.xaml` l.42.
23. **Printers page.** `admin/29-admin-printers.png`: every field looks editable while "Enabled" is unticked, the
    connection kind reads "EscPosRaw", "Print ₹ as" with placeholder "Rs." is unclear, TEST PRINT is disabled without
    a hint why. `Modules/Printing/PrinterSettingsView.xaml`.
24. **Login keeps "Retry" when online.** `cashier/01-cashier-sign-in.png`. `Modules/Auth/LoginView.xaml`.
25. **Notification preferences list is cut mid-row.** `cashier/16-cashier-notification-preferences.png`.
    `Shell/NotificationPreferencesView.xaml`.
26. **Command palette groups are not visible as groups.** `admin/31-admin-command-palette.png`: "Go to" and
    "Actions" entries are interleaved by score. `Shell/CommandPaletteViewModel.cs`, `ShellView.xaml`.
27. **Lists do not scroll to the record just created or changed.** `functions/12-menu-item-create-after.png`,
    `13-menu-sold-out-after.png`, `14-…`: Kothu Parotta sits below the fold, so the result of "create", "sold out" and
    "available" is invisible except for the toast and the button text; `10-tables-deactivate-after.png`: X01 likewise.
    `Modules/MenuAdmin/MenuItemsPageView.xaml`, `Modules/Admin/TablesView.xaml` (no `ScrollIntoView` on selection).
28. **"Cancel" next to "Cancel item".** `functions/30-order-cancel-line-dialog.png`: the abort button and the confirm
    button both start with "Cancel". `Modules/Tables/TableDetailsViewModel.cs` l.413 (`PromptAsync` confirm text).
29. **Refund is three dialogs in a row.** `functions/44-refund-partial-dialog.png`, `-dialog2`, `-dialog3`: amount →
    reason → manager approval, each a separate modal with no summary of what was entered before; one form would do.
    `Modules/Billing/ClosedBillsViewModel.cs` l.147-200.
30. **Identical toasts stack.** `functions/10-tables-deactivate-after.png`: two "Table X01 updated." toasts (rename
    then deactivate) — the message does not say what changed. `Modules/Admin/TablesViewModel.cs` l.113.
31. **Item created: editor title flips to "Edit item"** and stays open with "You can now add a picture", but nothing
    marks the new row in the list. `functions/12-menu-item-create-after.png`; `MenuItemsPageViewModel.cs` l.358-395.
32. **Backup FILE column clips the extension.** `functions/21-backup-page.png`: "hotelpos-20261016-111207.dum".
    `Modules/Admin/BackupView.xaml`.
33. **LOCKOUT column says "Normal".** `functions/05-users-unlock-before.png`: after five wrong passwords the API had
    locked waiter9 (server log, `20-server-page.png`) but the list still read "Normal" and Unlock was the only hint;
    "Normal" is an odd word for "not locked". `Modules/Admin/UsersView.xaml`, `UsersViewModel.cs`.
34. **Reset-password dialog has no policy hint or show-password toggle.** `functions/03-users-reset-password-dialog.png`;
    the editor shows the rules, the dialog does not. `Shell/MainWindow.xaml` dialog template.
35. **Test print feedback is easy to miss.** `functions/18-printers-test-print-after.png`: the failure is small grey
    text under the button, no toast and no colour. `Modules/Printing/PrinterSettingsView.xaml`.
36. **Audit action filter finds nothing for a prefix.** `functions/22-audit-filter-after.png`: user `cashier1` +
    action "Bill." returns 0 records although the same user has Bill.Settled / Bill.Finalized entries minutes earlier
    (`admin/24-admin-audit-log.png`); the placeholder "Action, e.g. Order.C…" suggests a prefix match. `Modules/Admin/AuditLogsViewModel.cs`.
37. **GST hub: totals and grid disagree.** `functions/24-gst-current-month-after.png`: the footer counts "4 invoices ·
    invoice value 2,615.00" while the B2B grid says "Nothing to show" (B2C invoices are in another section) and the
    error banner does not say what the error is. `Modules/Gst/GstHubView.xaml`.
38. **Report percentages with two decimals.** `functions/25-reports-05-kitchen.png`: "Late percent 20.00",
    "Average accept minutes 6.90". `Modules/Reports/ReportsViewModel.cs` (numeric columns formatted N2).
39. **Export toast has no "open" action and covers the totals.** `functions/26-reports-export-excel-after.png`:
    "Saved sales-daily-….xlsx." with no way to open the file or folder. `ReportsViewModel.DownloadAsync`.
40. **Devices LIVE column clips "Online"/"Offline".** `admin/23-admin-devices.png`: "Onlin", "Offlin".
    `Modules/Admin/DevicesView.xaml`.
41. **Settings list has no section navigation.** `admin/28-admin-settings.png`: a long alphabetical list; the
    Notifications section is at the very end (`28-admin-settings-notifications.png`) and "Save changes" is at the
    top, out of sight once scrolled. `Modules/Admin/SettingsView.xaml`.

What worked well: the shift bar, the X report and the count sheet are clear and consistent; the status-bar "1 print
failed · Retry · Discard" and the Print failures tile close the loop on a dead printer; manager approval is one
consistent dialog everywhere (discount, refund, reopen); the dark theme is complete on every screen captured.
