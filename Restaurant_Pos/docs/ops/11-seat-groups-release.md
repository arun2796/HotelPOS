# 11 — Seat groups, kitchen stock and takeaway billing: release notes, upgrade and rollback

This release adds seat groups on a table (several seats ordering together), a ticket and kitchen status for each seat
group, automatic separate bills per seat group, kitchen stock with a ledger, a Takeaway shortcut that bills while the
kitchen cooks, and a few cashier changes. It needs **three database migrations** (Phase 29, 30, 31). Read § 2 before upgrading.

## 1. What changes for the restaurant

| Area | Change |
|---|---|
| Ordering (Windows, Web, Android) | Seats are chips. Tap one seat, or several that share the order (`T01A+B`). A seat belongs to one seat group of an order; a taken seat is marked ● and selects its whole group. The server refuses an overlapping group. |
| Kitchen | Each seat group gets its own ticket per station (`1001-1-A`, `1001-1-CD`), so one group's status is never another's. The ticket shows the table, the merged table and the seat group. Tickets already open keep their old shared format. |
| Billing | **Request bill** can create one bill per seat group straight away. A bill with payments cannot be split. A void or whole-bill refund asks what happens to the food (return to kitchen stock, or waste). |
| Takeaway | Sidebar shortcut. **Bill & send to kitchen** makes the bill at once; cashier, manager and admin go straight to payment. Cashiers can now create orders. |
| Kitchen stock | One count per item; every change is a ledger row (opening, refill, order, cancel, waste, correction). **Kitchen stock** page for Admin, Manager and Kitchen. New permission `stock.adjust`. |
| Tables | Guests cannot exceed the table's seats (merged tables add theirs). Unmerge is refused while the order uses the given-back table's seats or the guests would not fit. |

## 2. Database changes

| Migration | Change | Data risk |
|---|---|---|
| `Phase29_StockLedger` | New table `StockMovements` (+ 2 indexes). | None: new, empty. |
| `Phase30_StockReorderLevel` | `MenuItems.ReorderLevel` (nullable). Grants `stock.adjust` to Admin, Manager, Kitchen and `orders.create` to Cashier. | None: nullable column; 4 new permission rows. |
| `Phase31_SeatGroupTickets` | `KitchenOrders.SeatCode` (nullable, 16). | None: existing tickets keep `NULL` = the old shared-ticket format. |

No existing row is rewritten. Old tickets, bills and payments are untouched.

## 3. Backup, upgrade, rollback

**Before:** follow `04-upgrade.md` § 1. Take a backup in the app (Administration → Backup) and copy it off the server.

**Upgrade:** run the new server installer. On start the API writes its own verified `pg_dump`
(`hotelpos-<date>-before-<migration>.dump`) and refuses to migrate if that copy cannot be written, then applies the three
migrations. Check `/health` and the top of the log.

**Rollback — safest (restore):** stop the service, install the previous server version, restore the pre-migration dump:

```
pg_restore --clean --if-exists --no-owner -h localhost -U postgres -d hotelpos <path>\hotelpos-<date>-before-<migration>.dump
```

Everything entered after the upgrade (orders, payments) is lost with a restore, so use it only for a failed upgrade, not days later.

**Rollback — keep the new data (down-migrate):** with the service stopped,
`dotnet ef database update 20261124105508_Phase28_SplitBillBySeat --connection "<connection string>"`.
This removes the three changes and **drops the stock ledger** (`StockMovements`); the current stock counts on the menu items stay.
Orders, tickets, bills and payments are not touched. Then install the previous server version.

### Rehearsal on a copy of real data (2026-11-25)
The development database (5 653 orders, 7 251 tickets, 5 635 bills, 5 636 payments) was backed up with `pg_dump -Fc`
(3.2 MB, 1 s), restored into a scratch database (2 s), upgraded, rolled back and upgraded again:

* Row counts and an MD5 of ids, statuses and totals for orders, order items, tickets, bills and payments were **identical** before the upgrade, after it, and after the rollback.
* After the upgrade: `StockMovements` exists, `MenuItems.ReorderLevel` exists, 0 of 7 251 tickets have a seat group (old format), role permissions 77 → 81.
* After the rollback to Phase 28: the table and both columns are gone, permissions are back to 77, all data unchanged.
* The scratch database was dropped afterwards.

## 4. Manual test procedure (use on a real terminal before go-live)

**Windows waiter** (a 4-seat table, 4 guests)
1. Open the table → New order. Four seat chips (A–D), none marked. Tooltip of each says "free".
2. Tap A, add a dish → A shows ●. Tap C then D (A is released), add a dish → C and D show ●.
3. Tap A → only A selected. Tap D → C and D selected together, A released. Tap B → only B. Tap Whole table → none.
4. Send. Kitchen Display shows two tickets, "Seat T01A" and "Seat T01C+D". Table details list each group's status.
5. Table → Add items: A, C, D are ●, B free. Tap D → C+D selected.
6. Request bill → "Separate bills?" → Separate bills. The counter has one bill per group.

**Android and web** — same steps 1–5 (the Android app is the same web client inside the app).

**Server conflict** — on a second terminal, add a dish for `B+C` to an order a phone has open; on the phone select seat B and send.
Expected: the phone shows "Seat T02B already belongs to the seat group T02B+C…" and the order is unchanged.

**Printing** — print the kitchen ticket of a merged table's seat group: table number, `SEAT <group>`, `Merged: T04 + T05`, and each dish with its seat.

## 5. Deployment steps (run only after the owner approves a production deployment)

Nothing in this section has been run against a production server. The 0.1.1 installers and APK dated 2026-11-23 do **not**
contain this release; rebuild them first (`build\publish.ps1` for the installers, `build\publish-android.ps1` for the APK; see
`04-upgrade.md` and `09-android-app.md`) and run the clean-VM test (`10-clean-vm-install-test.md`) on the new files.

1. **Quiet period.** Pick a time with no open orders. Close the business day if one is open.
2. **Back up.** On the admin terminal: Administration → Backup → copy the file off the server. Note the number of orders and bills on the Dashboard.
3. **Stop the terminals' work.** Ask staff to sign out. Open orders are kept; nothing needs to be closed.
4. **Server.** Run `HotelPOS-Server-Setup-<new version>.exe` with the database and admin fields **empty** (upgrade).
   The installer stops and restarts the **HotelPOS API** service (`Get-Service HotelPOSApi`).
5. **Migration sequence** (automatic, in this order, on API start, after the safety `pg_dump`):
   `Phase29_StockLedger` → `Phase30_StockReorderLevel` → `Phase31_SeatGroupTickets`. Read the top of
   `C:\ProgramData\HotelPOS\Api\logs\*.log`: three "Applying migration" lines, then "Now listening". Open `http://<server>:5000/health` → healthy.
6. **API restart check.** If the service did not start, do not retry in a loop: read the log, then follow § 3 (rollback).
7. **Windows terminals.** The Desktop app offers the update by itself (signed manifest); otherwise run `HotelPOS-Client-Setup.exe`
   on each terminal. Restart the app on each terminal (a running app keeps the old screens). Confirm the version in the footer.
8. **Android phones.** Install the new `HotelPOS-Staff.apk` over the old one (`http://<server>:5000/updates/HotelPOS-Staff.apk`
   in the phone's browser, or the APK file). Settings and sign-in are kept. Open the app, sign in and check that the order screen shows seat chips.
9. **Web / tablets.** Reload the page (Ctrl+F5, or clear the site data once). The seat chips appear after the reload.
10. **Smoke test** with § 4 on one waiter terminal, then on the kitchen display. Start with one table, then open the floor.
11. **Rollback** if § 4 fails: § 3, restore (preferred) or down-migrate.

## 6. UAT checklist (for the venue; tick each line with a person and a date)

- [ ] Waiter (Windows): § 4 steps 1–6 on a real table with real tablet/phone and the venue's kitchen display.
- [ ] Waiter (Android phone, **physical device**): § 4 steps 1–5.
- [ ] Two waiters, one table: second terminal takes `B+C` while the first has the table open; the first gets the "already belongs" message and the seats refresh.
- [ ] Kitchen: tickets arrive per seat group; start/ready one; the waiter's table detail shows each group's status; the other group is unchanged.
- [ ] Kitchen ticket **printed on the venue's thermal printer** (table, `SEAT <group>`, merged table, dish seat, additional-order ticket, no duplicates).
- [ ] Cashier: separate bills per group; a partial payment (cash then UPI); a refund; a void asking return-to-stock or waste.
- [ ] Day end: x-report and day-end figures match the counted drawer and the bills of the day.
- [ ] Kitchen stock: set Idli 40, Biryani 50, Grill 4; order, cancel, refill; all roles show the same count; sold-out shows "Sold out".
- [ ] Unplug the server's network for a minute: terminals show Reconnecting, then recover with no action; nothing is duplicated.
- [ ] Takeaway shortcut: bill appears at once; cashier goes straight to payment.

## 7. Known limitations

* **Thermal printing is not verified on hardware.** The kitchen ticket's PDF and ESC/POS bytes are tested; no thermal printer was available.
* **Android was driven on an emulator only**, not on a physical phone.
* **Installers and APK are not rebuilt** with this release (see § 5) and the **clean-VM install test has not been run** on them.
* **No production evidence.** Everything below was run on development databases and scratch databases.
* A seat-group chip list is a snapshot: another terminal's change appears after the next send is rejected (the builder re-reads the seats then) or when the screen is reopened. It does not update while the screen is open.
* Separate-bill labels `T01 (A)`, `T01 (B)` count the bills of the table, not the seats; the bill lists the seat group of each dish.
* Orders and tickets created before this release keep the old shared ticket; they cannot be split by seat group.
* The Desktop Kitchen board and UIA-based checks only list cards that are on screen; the header count (`NEW · 5`) is the reliable number.
* Down-migration drops the stock ledger (§ 3).

## 8. What was tested and how (this release)

Run on 2026-11-25 on the developer PC (Windows 11, .NET 10, PostgreSQL 18). Levels: **Automated** = a test run;
**Manual** = a person-style run of the real app, driven through Windows UI Automation, with screenshots in `artifacts\e2e\windows-uat\`;
**UAT / Production** = nothing yet (§ 6, § 7).

**Automated (final run, all five suites green)**

| Suite | Passed | Skipped |
|---|---|---|
| Domain | 422 | 0 |
| Application | 299 | 0 |
| Client (bUnit) | 234 | 0 |
| Desktop | 459 | 2 |
| Api (real PostgreSQL, one isolated database per test class) | 241 | 0 |

* The Client test `KitchenBoardTests.PressingAButton_PostsTheActionOnce_AndDoesNotOpenTheDetail` was **a test-timing problem, not a regression**:
  the test clicked a button element found before a re-render, so the click reached a stale handler. It fails the same way on
  older commits (checked in worktrees at HEAD and `eb7d866`). Fixed by finding and clicking inside one `InvokeAsync`.
* `ReconciliationApiTests`: an isolated database with a known drawer float; an order with several seat groups, a discount, separate
  bills, partial payments by cash, UPI and card, a refund, the shift x-report and the day-end summary. Bills, discounts, tax, payments,
  refunds, outstanding and day-end sales are asserted to agree.
* `StockAcceptanceApiTests`: Idli 40, Biryani 50, Grill 4 with five role clients and five live screens: 10 idlis (→30), billing,
  a second waiter's 10 (→20), refill (→40); all five roles see the same count; sold-out, cancellation and duplicate events.
* `SeatGroupProductionApiTests` / `SeatGroupApiTests`: merged tables, unmerge rules, partial payment, separate bills, refund, reprint,
  old-format tickets, live updates and reconnect, and a race of two terminals for the same seat. Kitchen ticket PDFs are rendered to
  `%TEMP%\hotelpos-kot-pdf` (`kot-1001-1-AB`, `kot-1001-1-C`, `kot-1001-2-addition`) and start with `%PDF`.
* New regression test `ARejectedAppend_ReloadsTheSeatGroups_SoTheChipsShowWhatIsReallyTaken` (see "Defect found" below).
* Test databases: no `hotelpos_apitests_*` database remained; only verified-idle leftovers had been dropped, and `hotelpos_dev` and `hotelpos_e2e` were not touched.

**Manual, real Windows apps (five instances: waiter, kitchen, cashier, manager, admin; one API; one scratch database)**

* Seats (waiter): four free chips; one seat selects only itself; a dish marks it taken; two free seats select together and become one group
  with the tooltip "already has dishes with the group T01C+D"; tapping a taken seat selects its whole group; a free seat is never mixed
  with a taken group (12 of 12 checks, `waiter-seat-results.txt`).
* Send: the kitchen window showed two live tickets (`1001-1-A`, `1001-1-CD`, each "Seat …"). Starting and finishing one moved only that
  ticket; the waiter's table detail showed "Seat T01A · preparing/ready" and "Seat T01C+D · waiting for the kitchen" live; the manager's
  Orders list and activity feed updated; the admin dashboard showed the live order status.
* Reopen (Add items): ownership came back from the server (A, C and D taken, B free).
* Conflict: the second device took `C+D`; the stale first terminal sent `B+C`; the server refused ("Seat T02C already belongs to the seat
  group T02C+D…") and saved nothing.
* Billing: Request bill → "Separate bills?" → two bills reached the cashier window live ("Table T01 (A) asked for the bill").
* Reconnect: with the API stopped, waiter, kitchen and cashier showed "Reconnecting…"; after it restarted all five recovered without any
  action, and a dish added by another device afterwards arrived as a new kitchen ticket (board count matched the server).

**Defect found by the manual run (fixed)**: after a rejected send for an existing order, the seat chips kept the old snapshot, so a seat
the other terminal had taken still looked free. The builder now re-reads the order's seat groups after a rejection
(`OrderBuilderViewModel`), verified again in the real app. The pre-fix behaviour was seen in the real app, not in a failing test run.

**Not done / not verified**: thermal printing, a physical Android phone, a multi-PC network test, the installers and clean-VM test, any production
deployment. Kitchen ticket layout was verified from the PDF and the ESC/POS test, not on paper.
