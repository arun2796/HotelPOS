# HotelPOS — demo pack (presentation and video storyboard)

Generated 19 Oct 2026 from the real application (desktop client, browser PWA) against a Development API with the seeded
demo data on `main` after Phase 22. Nothing is mocked: every picture is a screenshot of a real screen after a real
action, and every action is recorded with its result in the two test records below. 129 pictures, 123 steps, all
passing after the fixes listed at the end.

| Chapter | Folder | Client | Signed in as | Record |
|---|---|---|---|---|
| 1. Set up the restaurant | `01-admin-setup/` (31) | Desktop (WPF, 1366×800) | admin | `desktop-test-record.md` |
| 2. Take orders on the floor | `02-waiter-phone/` (38) | Browser PWA, phone 390×844 | waiter1 | `web-test-record.md` |
| 3. Cook and serve | `03-kitchen-tablet/` (13) | Browser PWA, tablet 1024×768 | kitchen1 | `web-test-record.md` |
| 4. Bill and collect | `04-cashier-desktop/` (24) | Desktop (WPF, 1366×800) | cashier1 | `desktop-test-record.md` |
| 5. Run the business | `05-manager-desktop/` (23) | Desktop (WPF, 1366×800) | manager1 | `desktop-test-record.md` |

Pictures are numbered in the order to show them. The same numbering works as the slide order and as the shot list of
the video; the caption under each picture is the narration line. Pictures marked **(re-shot)** were captured again
after the bug found in the first pass was fixed the same night; see "What the pass found" at the end.

## Suggested running order for the video (about 8 minutes)

1. **Opening (30 s).** One restaurant, five roles, three clients on one LAN: the admin and the cashier on Windows
   terminals, waiters on their phones, the kitchen on a tablet, the manager on any of them. Everything updates live.
2. **Chapter 1 — set up (1 min).** Users and roles, the floor (sections, tables, the new floor-plan editor), the menu
   with pictures, payment methods including Wallet, printers, settings. Show the audit log at the end: every change
   is recorded with who and from which terminal.
3. **Chapter 2 — the waiter (2 min).** Sign in on the phone, the tables as a list and as the floor map, a reserved
   table, a dine-in order (search, favourites, options and a note), send to kitchen, a takeaway order with no table,
   hold a draft and resume it, print the KOT from the phone.
4. **Chapter 3 — the kitchen (1 min).** The board fills as orders arrive, the ticket detail with item pictures,
   accept → start → ready, the waiter's phone shows Ready, serve, request the bill.
5. **Chapter 4 — the cashier (1.5 min).** Open the shift, the bill queue (the takeaway order shows as TA-…), a bill
   with a discount, cash, split and Wallet payments, paid, print, closed bills, a pay-out and an expense, X report,
   close the shift.
6. **Chapter 5 — the manager (1.5 min).** The dashboard (sales, orders, average order value, occupancy, expenses
   today; hourly sales; who is on duty right now), reservations, reports (daily, hourly, profit & loss, payments)
   exported to Excel and PDF, Day End pre-check, GST for the month, audit log.
7. **Close (30 s).** Offline-tolerant clients, licence-controlled terminals, backups and self-update on the server.

## Chapter 1 — Set up the restaurant (`01-admin-setup/`, desktop, admin)

| # | Picture | Narration |
|---|---|---|
| 01 | `01-sign-in.png` | The desktop client signs in with a username and password; the terminal name is registered on the server and counts against the licence. |
| 02 | `02-dashboard.png` | The admin lands on the same live dashboard the manager uses. |
| 03 | `03-users-list.png` | Users: every member of staff with their role and status. |
| 04 | `04-users-new-dialog.png` | Adding a waiter takes a name, a username and a role. |
| 05 | `05-users-created.png` | The new user appears at once and can sign in on a phone. |
| 06 | `06-roles-permissions.png` | Roles & permissions: each role is a set of permissions grouped by area (tables, menu, orders, kitchen, billing, cash, finance, reports, administration). |
| 07 | `07-roles-permissions-reservations.png` | Further down: the reservation and floor-plan permissions added in Phase 22. **(re-shot)** |
| 08 | `08-roles-permissions-expenses.png` | And the expenses permissions. **(re-shot)** |
| 09 | `09-devices.png` | Devices: every terminal, phone and tablet that has signed in, with its kind, status and licence seat. |
| 10 | `10-sections-tab.png` | Sections & Tables: the dining areas. |
| 11 | `11-tables-editor.png` | A table has a code, a name, a capacity and a section. |
| 12 | `12-tables-tab.png` | All tables of the restaurant with their live status. |
| 13 | `13-floor-plan-section.png` | Floor plan: the tables of a section on a canvas, placed on a grid by default. **(re-shot)** |
| 14 | `14-floor-plan-tile-selected.png` | Select a table to move or resize it with the handles; snap-to-grid keeps it tidy. **(re-shot)** |
| 15 | `15-floor-plan-rotated.png` | Rotate 90° and switch between rectangle and round. **(re-shot)** |
| 16 | `16-floor-plan-saved.png` | Save: every terminal and phone gets the new plan immediately. **(re-shot)** |
| 17 | `17-menu-categories.png` | Menu: categories in display order. |
| 18 | `18-menu-items.png` | Items with price, tax, kitchen station and availability. |
| 19 | `19-menu-item-editor.png` | Editing an item: price, tax, station, picture. |
| 20 | `20-menu-item-editor-modifiers.png` | Modifier groups attach to an item (sizes, extras, spice level). |
| 21 | `21-menu-modifiers.png` | The modifier library. |
| 22 | `22-payments-discounts.png` | Payment methods (Cash, Card, UPI, Wallet) and the discount presets with their approval rules. |
| 23 | `23-restaurant-settings.png` | Restaurant settings: name, address, GSTIN, business-day start, rounding, kitchen warnings. |
| 24 | `24-printers.png` | Printers per terminal: receipt, kitchen stations, test print. |
| 25 | `25-expense-categories.png` | Expense categories used by the Expenses screen and the Profit & Loss report. |
| 26 | `26-licence.png` | Licence: seats, expiry, activation. |
| 27 | `27-server.png` | Server: version, health, hosting, self-update. |
| 28 | `28-backup.png` | Backups: create, schedule, restore. |
| 29 | `29-audit-log.png` | Audit log: who changed what, when, from which terminal. |
| 30 | `30-command-palette.png` | Ctrl+K jumps to any screen, table or menu item. |
| 31 | `31-dashboard-dark.png` | The same application in the dark theme. |

## Chapter 2 — Take orders on the floor (`02-waiter-phone/`, phone, waiter1)

| # | Picture | Narration |
|---|---|---|
| 01 | `01-sign-in.png` | The waiter signs in on the phone; the app installs as a PWA and remembers the device. |
| 02 | `02-home-kpis.png` | Home: my orders today, my sales, tables I am serving. |
| 03 | `03-tables-list.png` | Tables as a list grouped by section, available first. |
| 04 | `04-tables-list-reservation-badge.png` | A table with a reservation due soon carries a badge. |
| 05 | `05-tables-map-ground-floor.png` | The same tables on the floor plan drawn by the admin. |
| 06 | `06-tables-map-other-section.png` | A section without a plan falls back to the list. |
| 07 | `07-table-details-reservation.png` | Table details with the reservation: name, party size, time. |
| 08 | `08-table-seated.png` | Seat the guests: the reservation becomes Seated and the table Occupied. |
| 09 | `09-order-type-chips.png` | New order: Dine-in, Takeaway or Delivery. |
| 10 | `10-order-search.png` | Search the menu by name. |
| 11 | `11-favourites.png` | Favourites: what this waiter orders most. |
| 12 | `12-item-options-note.png` | Options, quantity and a note for the kitchen in one sheet. |
| 13 | `13-cart.png` | The cart bar keeps the running total. |
| 14 | `14-review-sheet.png` | Review before sending. |
| 15 | `15-sent-to-kitchen.png` | Sent: the kitchen tablet shows it instantly. |
| 16 | `16-order-kitchen-tickets.png` | Order details: the kitchen tickets per station and the Print KOT button. |
| 17 | `17-print-kot-pressed.png` | Print KOT from the phone. |
| 18 | `18-print-kot-pdf-tab.png` | The KOT opens as a thermal-width PDF; the print log records the copy. |
| 19 | `19-order-items.png` | Items tab with the note. |
| 20 | `20-takeaway-builder.png` | A takeaway order needs no table. |
| 21 | `21-takeaway-sent.png` | It is labelled TA-… everywhere: kitchen, cashier, receipt. |
| 22 | `22-draft-saved.png` | Save draft holds an order on the server. **(re-shot)** |
| 23 | `23-held-tab.png` | My orders → Held lists held orders for the whole team. **(re-shot)** |
| 24 | `24-held-tab.png` | A held order. |
| 25 | `25-held-resumed.png` | Resume it in the builder. |
| 26 | `26-held-resumed-sent.png` | And send it. |
| 27 | `27-my-orders.png` | My orders: active orders with their status. |
| 28 | `28-takeaway-details.png` | Takeaway order details: channel, items, tickets, Print KOT. **(re-shot)** |
| 29 | `29-bell-panel.png` | The bell: ready, served and bill notifications. |
| 30 | `30-settings.png` | Settings: theme, sounds, station. |
| 31 | `31-tables-dark.png` | Dark theme. |
| 32 | `32-my-orders-ready.png` | The kitchen marked the order Ready: the phone shows it. |
| 33 | `33-served.png` | Mark served. |
| 34 | `34-bill-requested.png` | Request the bill: the cashier's queue gets it. |
| 35 | `35-bell-ready.png` | The Ready notification in the bell. |
| 36 | `36-offline-banner.png` | Wi-Fi drops: the app says so and keeps working. |
| 37 | `37-offline-my-orders.png` | Orders stay readable offline. |
| 38 | `38-back-online.png` | Back online, everything refreshes. |

## Chapter 3 — Cook and serve (`03-kitchen-tablet/`, tablet, kitchen1)

| # | Picture | Narration |
|---|---|---|
| 01 | `01-sign-in.png` | The kitchen signs in on the tablet. |
| 02 | `02-board-new-tickets.png` | The board: New, Preparing, Ready, with counts and ages. |
| 03 | `03-board-new-tickets-scrolled.png` | The dine-in and the takeaway tickets (Counter chip instead of a table). |
| 04 | `04-ticket-detail-pictures.png` | Ticket detail with item pictures. **(re-shot)** |
| 05 | `05-ticket-detail-takeaway.png` | A takeaway ticket. |
| 06 | `06-station-show-pictures.png` | Station settings: pick the station, show pictures on the cards. |
| 07 | `07-board-thumbnails.png` | The board with thumbnails. **(re-shot)** |
| 08 | `08-accepted.png` | Accept. |
| 09 | `09-preparing.png` | Start preparing. |
| 10 | `10-ready.png` | Ready: the waiter's phone is told. |
| 11 | `11-completed.png` | Completed tickets. |
| 12 | `12-bell-panel.png` | Kitchen notifications. |
| 13 | `13-board-dark.png` | Dark theme for a dim kitchen. |

## Chapter 4 — Bill and collect (`04-cashier-desktop/`, desktop, cashier1)

| # | Picture | Narration |
|---|---|---|
| 01 | `01-sign-in.png` | The cashier signs in on the billing terminal. |
| 02 | `02-cashier-home.png` | Cashier home: bills waiting, shift status. |
| 03 | `03-shift-already-open.png` | A cash shift is open with its opening float. |
| 04 | `04-queue.png` | The queue: every bill requested by the waiters. |
| 05 | `05-queue-takeaway.png` | The Takeaway chip: counter orders labelled TA-…. |
| 06 | `06-workspace-bill.png` | Workspace: queue, bill, payment side by side. |
| 07 | `07-discount.png` | A discount with its reason (and approval when the rule asks). |
| 08 | `08-finalized.png` | Finalize: the invoice number is assigned, GST stamped. |
| 09 | `09-cash-payment.png` | Cash (F2) with tendered amount and change. |
| 10 | `10-split-payment.png` | Split a bill across methods. |
| 11 | `11-wallet-payment.png` | Wallet with its reference. |
| 12 | `12-paid.png` | Paid. |
| 13 | `13-print-receipt-warning.png` | Print the receipt (the demo printer is not reachable, so the warning shows). |
| 14 | `14-queue-paid-chip.png` | The queue's Paid chip. |
| 15 | `15-closed-bills.png` | Closed bills with search, reprint and refund. |
| 16 | `16-print-log.png` | Print log: every copy, with retry. |
| 17 | `17-pay-out.png` | A cash pay-out from the drawer. |
| 18 | `18-expenses-list.png` | Expenses: list with date, category and void filters. |
| 19 | `19-expense-record-dialog.png` | Record an expense, optionally linked to a drawer pay-out. |
| 20 | `20-expense-recorded.png` | Recorded. |
| 21 | `21-x-report.png` | X report: the shift so far. |
| 22 | `22-notifications-bell.png` | Notifications. |
| 23 | `23-close-shift.png` | Close the shift: counted cash against expected. |
| 24 | `24-close-shift-z-report.png` | The Z report. |

## Chapter 5 — Run the business (`05-manager-desktop/`, desktop, manager1)

| # | Picture | Narration |
|---|---|---|
| 01 | `01-sign-in.png` | The manager signs in. |
| 02 | `02-dashboard-kpis.png` | Five KPI cards: sales, orders, average order value, occupancy, expenses today. |
| 03 | `03-dashboard-middle.png` | Live order status, active orders, table status. |
| 04 | `04-dashboard-bottom.png` | Seven-day sales, hourly sales, payments, top items, staff on duty right now. |
| 05 | `05-orders.png` | Orders: every order by status with its channel. |
| 06 | `06-reservations-list.png` | Reservations for the day. **(re-shot)** |
| 07 | `07-reservation-new-form.png` | A new reservation: table, name, phone, party size, time. **(re-shot)** |
| 08 | `08-reservation-created.png` | Created: the table badge appears on every client. **(re-shot)** |
| 09 | `09-reservation-seated.png` | Seat it from here or from the waiter's phone. **(re-shot)** |
| 10 | `10-tables-map.png` | Tables as the floor plan. |
| 11 | `11-tables-list.png` | Or as a list. |
| 12 | `12-kitchen-display.png` | The manager can watch the kitchen. |
| 13 | `13-report-daily-sales.png` | Reports: daily sales. |
| 14 | `14-report-hourly-sales.png` | Hourly sales. |
| 15 | `15-report-profit-loss.png` | Profit & Loss: sales minus expenses by category. |
| 16 | `16-report-payments.png` | Payments by method, Wallet included. |
| 17 | `17-report-export-excel-before.png` | Every report exports to Excel… |
| 18 | `18-report-export-excel-saved.png` | …saved with the restaurant's letterhead. |
| 19 | `19-report-save-pdf.png` | And to PDF. |
| 20 | `20-day-end-precheck.png` | Day End pre-check: what must be closed before the day can. |
| 21 | `21-gst-current-month.png` | GST for the month, ready for the return. |
| 22 | `22-audit-log-filter.png` | Audit log filtered by action. |
| 23 | `23-dashboard-dark.png` | Dark theme. |

## What the pass found

The point of driving every function for real was to find what a demo audience would find. Six product bugs
surfaced and were fixed on `main` the same night, each with a regression test:

1. Menu pictures never loaded in the browser (the picture URL was built as a `file://` path).
2. A takeaway order's details page on the phone redirected to a route that does not exist.
3. "Save draft" on the phone never created a server-side held order, so the Held tab stayed empty.
4. The desktop Expenses screen reloaded itself in an endless loop, flooding the API.
5. The desktop Roles page did not list the Phase 22 permissions and stripped them from a role on save.
6. The desktop Reservations page laid itself out wrongly at 1366×800.

Smaller UI/UX notes (clipped columns, chips, stale demo data, slow steps) are listed at the end of each record.

## Test records

- `desktop-test-record.md` — every desktop step (admin, cashier, manager): action, expected, result, screenshot.
- `web-test-record.md` — every web step (waiter, kitchen); `web-console-errors.json` and `web-small-targets.json`
  hold the console errors and the touch targets under 48 px seen during the run.

## Regenerating

1. Start the API in Development mode (`dotnet run --project src/HotelPOS.Api`; it listens on :5000 and serves the
   PWA at /app) with the demo seed. Accounts: `admin` (the configured admin password), `manager1`, `cashier1`,
   `waiter1`, `kitchen1` with the demo password.
2. Desktop chapters: `HOTELPOS_LIVE_SCREENS=1 HOTELPOS_LIVE_SCREENS_ROLES=demo HOTELPOS_LIVE_TABLE=T05
   HOTELPOS_ADMIN_PASSWORD=… HOTELPOS_LIVE_SCREENS_DIR=<repo>/docs/demo` then
   `dotnet test tests/HotelPOS.Desktop.Tests -p:OutDir=<repo>/tests/HotelPOS.Desktop.Tests/bin/demo/ --filter "FullyQualifiedName~LiveFlowScreenshots"`
   (code: `tests/HotelPOS.Desktop.Tests/Rendering/LiveFlowScreenshots.Demo.cs`).
3. Web chapters: from a scratch folder outside the repo with Playwright installed (`npm i playwright`):
   `HOTELPOS_TABLE=T04 HOTELPOS_SPARE_TABLE=T03 OUT_DIR=<repo>/docs/demo node <repo>/docs/ui-ux/tools/demo-web-flow.mjs`
   (the script picks the first free table when the requested one is busy).
4. Afterwards deactivate the `SCREENS-*` / `WEB-*` terminals the runs registered (Admin → Devices) so they do not
   use licence seats.

5. Narrated videos (one MP4 per role + `HotelPOS-full-walkthrough.mp4`, about 13 min, with a Windows voice reading the
   narration column): from the Playwright scratch folder, `node <repo>/docs/ui-ux/tools/demo-video.mjs` writes them to
   `artifacts/demo-video/` (gitignored); `narration-script.md` there lists every spoken line. `VOICE="Microsoft David Desktop"`
   and `RATE=-1` change the voice and pace; delete `artifacts/demo-video/work` to rebuild from scratch.

6. Narrated videos of the LIVE application (no still pictures; `artifacts/demo-video-live/`): the same two harnesses record
   themselves while they drive the app and pause after each step for its narration (`docs/ui-ux/tools/demo-narration.json`,
   simple English, one line per step). From the Playwright scratch folder: `node <repo>/docs/ui-ux/tools/demo-video-live.mjs --prepare`
   (speaks the lines, writes `work/durations.json`), then the desktop run with `HOTELPOS_DEMO_RECORD=<out>/record`
   `HOTELPOS_DEMO_NARRATION=<out>/work/durations.json` `HOTELPOS_FFMPEG=<ffmpeg.exe>` added to step 2, then the web run with
   `RECORD_DIR=<out>/record HOTELPOS_DEMO_NARRATION=…` added to step 3, then `node …/demo-video-live.mjs` to cut and join
   (`01-admin.mp4` … `05-manager.mp4`, `HotelPOS-full-walkthrough.mp4`, `narration-script.md`). Tables DP1/DP2 must be free
   before the manager chapter (its reservation is seated on one of them): settle their bills and cancel stale bookings first.

7. Promo-style edition of the live videos (design after the owner's BillingHub reference clip: dark scenes, headline with an
   accent word, the app in a window or phone mockup, subtitle + voice, a quiet synthesized music bed ducked under the voice):
   after steps 1–3 of item 6, `node <repo>/docs/ui-ux/tools/demo-video-promo.mjs` writes `<out>/promo/01-admin.mp4` …
   `05-manager.mp4`, one video per app under `promo/apps/` (desktop, phone, kitchen tablet) and the full walkthrough.
   `MUSIC=<file>` swaps in a licensed track, `MUSIC_LEVEL` (default 0.16) sets how loud it is under the voice. `THEME` (navy, emerald) colours the role and app videos;
   `FULL_THEME` colours the full walkthrough on its own (the owner chose emerald for it).

Android: the same PWA pages run inside the Android app (`docs/ops/09-android-app.md`); phone screenshots for the video
are best taken on the device itself after the owner's device test.
