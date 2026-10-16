# Desktop client: screenshots of the real cashier, manager and admin flows

Captured on 16 Oct 2026 from the **real WPF client against the running Development API** (`http://localhost:5000`,
seeded demo data), 1366×800, light theme unless the name says `dark`. Regenerated the same day after the Phase 20.11
fixes (second run: table T03, demo records suffixed `6` for the admin edits, i.e. `waiter96`, `Terrace6`, `X016`, `Chef specials6`,
`Kothu Parotta6` (code C06), and `5` for the customer `Ravi Kumar5`; the API still ran the pre-fix server code, so the print-failed notice number
comes from the client-side fallback). Nothing here is sample data: every screen is
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
`cashier`, `functions-manager` and `functions-cashier*` create orders and bills on the table in `HOTELPOS_LIVE_TABLE`
(default **T02**); `functions-admin-edits` creates user `waiter9`, section `Terrace`, table `X01`, category `Chef
specials` and item `Kothu Parotta`, each with the `HOTELPOS_LIVE_RUN` suffix appended, so set a new suffix (and a free
table) for every replay or the saves report duplicates. A capture log is written to `%TEMP%\hotelpos-live-screens.log`.

## `cashier/` — the cashier flow (cashier1 on BILLING-01), 16 screens

The descriptions below are from the first run; the regenerated pictures show the same steps on **T03** (order #1012,
invoice INV-202610-000008 and later), with the fixes of 20.11 applied.

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

The regenerated pictures use the suffix-`6` records (`waiter96`, `Terrace6`, `X016`, `Chef specials6`,
`Kothu Parotta6`, customer `Ravi Kumar5`) and table T03; the rows below keep the first run's names.
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
| cashier | Bill: finalize order #1008 (invoice issued) and REOPEN it | `46-bill-reopen-invoiced-*` (dialogs: reason, Manager approval; the first run's `-dialog3`, a retry prompt after the approver was rate-limited, did not recur) | Invoice cancelled, order back with the waiter. A **settled** bill cannot be reopened by design (REOPEN is offered only while nothing is paid; settled bills are refunded), so the invoiced-unpaid case was used. Order #1008 was settled through the API afterwards to free T02. |

## UI/UX issues

Ordered roughly by how much they would bother a cashier or manager at the counter. 1–26 from the flow pass, 27 onwards
from the function pass. **Fixed** entries were corrected in Phase 20.11 and the screenshot named is the regenerated one
(second run: table T03, demo records with the suffixes `6` and `5`); the rest are still open.

1. **Fixed — Toasts cover modal and page buttons.** Toasts now appear top-centre under the top bar and are dismissed
   when the page changes: `cashier/08-cashier-paid.png` (one toast, clear of NEXT BILL), `cashier/14-cashier-close-shift.png`
   (the pay-out toast sits over the dialog title, not the buttons), `functions/06-roles-edit-before.png` (no Users
   toast left over), `functions/12-menu-item-create-after.png`. Was: the bottom-right host sat above the dialog layer and
   the page's action row.
2. **Fixed — The terminal is notified about its own actions.** `cashier/05-cashier-bill-detail.png` has no "bill
   claimed by BILLING-01" toast and `cashier/08-cashier-paid.png` shows only "Paid. Table T03 is free."; the bell
   (`cashier/15-cashier-notifications-bell.png`) lists the bill request and the print failure only. The mapper drops a
   claim by this device; the router drops the payment echo the bill screen announced.
3. **Fixed — Bill lines hide the chosen options and notes.** `cashier/05-cashier-bill-detail.png`: "Spicy, Extra raita
   (+30.00 each)" and "Medium · Note: Less oil" under the lines (loaded from the order).
4. **Fixed — Print-failure notices use the internal bill id.** `cashier/15-cashier-notifications-bell.png`: "Invoice
   INV-202610-000008 did not print on BILLING-01 …". The server now sends the number; the client falls back to the
   queue's copy of it.
5. **Fixed — Misleading help in the no-printer toast for cashiers.** `cashier/09-cashier-print-receipt-warning.png`:
   "Ask a manager to set one up under Printers." for a cashier; managers and admins read "Set one up under Printers in
   the menu."
6. **Fixed — Print Log clips the two columns that matter.** `cashier/10-cashier-print-log.png`: DOCUMENT and WHY are
   wider, end in an ellipsis and show the full text as a tooltip.
7. **Fixed — Denomination sheet does not fit the dialog.** `cashier/03-cashier-open-shift.png`, `14`: 40 px rows, all
   seven notes visible.
8. **Fixed — Dashboard panels are below the fold.** `admin/17-admin-dashboard.png`: six tiles in one row and a shorter
   chart; Kitchen performance, Staff and Terminals start on the first screen.
9. **Fixed — Inconsistent currency formatting on the dashboard.** `admin/17-admin-dashboard.png`: "3,303.00" / "1,170.00"
   in the payments legend.
10. **Fixed — "Today in blue" / "0" labels.** `admin/17-admin-dashboard.png`: "Today is the solid bar; …" and empty days
    have no label.
11. **Terminals tile is dead for managers.** `manager/17-manager-dashboard-panels.png`: unchanged (the devices endpoint
    is Admin-only).
12. **Partly fixed — Audit log clips and mislabels.** `admin/24-admin-audit-log.png`: TIME is 150 px, ACTION trims with a
    tooltip, the detail shows "On terminal: …" only when there is one, the action filter box is wider. Still open: the
    preset says "Today" while the range reads a month.
13. **Fixed — Three names for the cashier's home.** `cashier/02-cashier-home.png`: "Cashier home" in the sidebar, top bar
    and heading; the My shift tile says "open one from the shift bar at the top".
14. **"Discount DISCOUNT".** `cashier/05-cashier-bill-detail.png`: unchanged.
15. **Fixed — Sidebar user block truncates name and role.** Every screen: "Priya (Cashier)" / "Cashier" in full; the
    three buttons moved to their own row.
16. **Fixed — Page descriptions are cut in the top bar.** `admin/26-admin-gst-hub.png`, `functions/19-licence-page.png`:
    the description takes the whole title column and shows a tooltip when it still has to trim.
17. **Fixed — Closed bills: unlabeled filters.** `cashier/11-cashier-closed-bills.png`: "Business day" and "Table, invoice
    or order no." hints; the print history says "(did not print)".
18. **Fixed — GST hub opens on an error.** `admin/26-admin-gst-hub.png`: opens on the current month; a month without
    invoices shows no error banner. Still open: the lower-cased acronyms in the headers.
19. **Fixed — Plurals.** `admin/27-admin-day-end-checks.png`: "1 unpaid bill", "1 open cash shift"; Reports "1 row";
    bill subtitle "1 guest".
20. **Fixed — Unstyled checkbox column.** `admin/19-admin-menu-items.png`: IN STOCK uses the themed CheckBox.
21. **Opening the item editor squeezes the list.** `admin/20-admin-menu-item-editor.png`: unchanged.
22. **Same filter, two labels.** Unchanged.
23. **Printers page.** `admin/29-admin-printers.png`: unchanged apart from the test-print result (35).
24. **Fixed — Login keeps "Retry" when online.** `cashier/01-cashier-sign-in.png`: Retry is hidden while the server is
    reachable.
25. **Notification preferences list is cut mid-row.** `cashier/16-cashier-notification-preferences.png`: unchanged.
26. **Command palette groups are not visible as groups.** `admin/31-admin-command-palette.png`: unchanged.
27. **Fixed in the app, not visible here — Lists do not scroll to the record just created or changed.** The Tables,
    Sections, Users, Customers, Categories and Menu items grids now call `ScrollIntoView` on the selected row. The
    harness renders a window that is never shown, and WPF defers a DataGrid's `ScrollIntoView` until the control is
    loaded, so `functions/12-menu-item-create-after.png` and `13-menu-sold-out-after.png` still show the top of the
    list; check it in the running app.
28. **Fixed — "Cancel" next to "Cancel item".** `functions/30-order-cancel-line-dialog.png`: "Keep item" / "Cancel item".
29. **Refund is three dialogs in a row.** `functions/44-refund-partial-dialog3.png`: still three (one input per dialog
    and the API needs the reason), but the approval dialog now repeats the amount and the reason.
30. **Fixed — Identical toasts stack.** `functions/09-tables-rename-after.png`, `10-tables-deactivate-after.png`: "Table
    X016 renamed to "Terrace window6"." then "Table X016 deactivated." (and toasts no longer outlive the page).
31. **Fixed in the app, not visible here — Item created: nothing marks the new row.** The new item is selected and
    scrolled into view (same caveat as 27); the editor still stays open for the picture.
32. **Fixed — Backup FILE column clips the extension.** `functions/21-backup-page.png`: wider, trims with a tooltip.
33. **Fixed — LOCKOUT column says "Normal".** `functions/05-users-unlock-before.png`: "Not locked" / "Locked".
34. **Reset-password dialog has no policy hint or show-password toggle.** Unchanged.
35. **Fixed — Test print feedback is easy to miss.** `functions/18-printers-test-print-after.png`: a red (or green)
    banner under the button plus a toast.
36. **Audit action filter finds nothing for a prefix.** `functions/22-audit-filter-after.png`: unchanged (the server
    matches the action exactly).
37. **Fixed — GST hub: totals and grid disagree.** `functions/24-gst-current-month-after.png`: no error banner on a
    month without invoices; the footer and the empty section are otherwise as before (B2C invoices live in another
    section).
38. **Fixed — Report percentages with two decimals.** `functions/25-reports-05-kitchen.png`: "20%", "6.9".
39. **Fixed — Export toast has no "open" action.** `functions/26-reports-export-excel-after.png`: "Saved … · Open".
40. **Fixed — Devices LIVE column clips "Online"/"Offline".** `admin/23-admin-devices.png`.
41. **Fixed — Settings list has no section navigation.** `admin/28-admin-settings.png`: a section list on the left
    (Restaurant … Backup, Notifications) scrolls the list to the heading.

What worked well: the shift bar, the X report and the count sheet are clear and consistent; the status-bar "1 print
failed · Retry · Discard" and the Print failures tile close the loop on a dead printer; manager approval is one
consistent dialog everywhere (discount, refund, reopen); the dark theme is complete on every screen captured.
