# Phase 13 — Shifts, Cash Register and Day End

Status: In progress (started 2026-10-14) · Depends on: Phase 12 (written as CQRS commands/queries from the start) · Reference: BillingHub
`Entities/CashShift`, `CurrencyDenomination`, `Features/CashRegister`, `RunDayEndCommand`, `GetDayEndPrecheckQuery`,
`DayCloseGuard`

## 1. Goal

Account for every rupee at each billing counter. A cashier opens a shift on a billing terminal with a counted float.
Every cash payment and refund on that terminal belongs to the open shift, and pay-ins and pay-outs are recorded with
a reason. An X report shows the shift so far. The shift closes with a denomination count against the expected cash
(short/excess explained, large differences approved by a manager) and a Z report. When service ends, the manager
runs Day End: pre-checks, a backup snapshot, the day's report pack, and a lock so the closed business day accepts no
more bills or payments.

## 2. Prerequisites

- Phase 6 (bills, payments, refunds, idempotency), Phase 7 (printing), Phase 8 (reports, business day), Phase 9
  (permissions, approvals, audit), Phase 10 (backups), Phase 12 (CQRS pipeline: authorization, validation,
  transaction, audit behaviours).
- Phase 14 for the PDF/Excel Day End pack. Until it lands, the pack is the existing reports as CSV, and the Z report
  prints through the Phase 7 renderers.

## 3. Scope

**In**
- **Denominations** (master): ₹2000 … ₹1 notes and coins, active flag, display order; seeded with current Indian
  currency.
- **Shift** per billing terminal: open (float counted by denomination), current shift on the terminal, pay-in, pay-out
  and till-voucher movements with reason, X report (no reset), close with counted denominations, expected vs counted,
  short/excess with reason, Z report. Optional **blind count** (setting): the cashier counts before seeing the
  expected amount.
- **Handover**: close one cashier's shift and open the next on the same terminal with the counted cash as float, in
  one step.
- **Force close** by a manager for an absent cashier (reason, audited).
- **Cash needs a shift**: a cash payment or cash refund is refused on a terminal without an open shift
  (`SHIFT_REQUIRED`); card and UPI do not need one.
- **Approvals**: a variance above `CashVarianceApprovalLimit` (setting, default ₹100) needs a manager approval token
  (Phase 9 mechanism), as does reopening a closed shift.
- **Day End**: pre-check (open orders, unpaid or open bills, open shifts, pending kitchen tickets for the day), with
  an explicit, reasoned override for each blocking item. Then a Day End backup snapshot (Phase 10 `BackupService`,
  verified), a totals snapshot, the report pack, and the business-day lock. A manager may reopen a closed day with
  approval and reason; Day End can run again afterwards.
- **Owner collections**: money taken outside the till (owner's UPI, cheque) recorded against the day as non-till
  collections, so Day End totals reconcile.

**Out**
- Bank reconciliation, petty-cash bookkeeping beyond till vouchers, cash drawer hardware kick (later with printers).

## 4. Deliverables by project

| Project | Deliverables |
|---|---|
| Domain | `CashShift` (state machine Open → HandedOver/Closed/ForceClosed; Reopened back to Open), `CashMovement`, `Denomination`, `DenominationCount` value type, `BusinessDayClose`, `ExpectedCash` calculation, `BusinessDay` helper (start time, default day) |
| Application | `Features/Shifts/*` (OpenShift, GetCurrentShift, AddMovement, GetXReport, CloseShift, HandOverShift, ForceCloseShift, ReopenShift), `Features/DayEnd/*` (GetDayEndPrecheck, RunDayEnd, ReopenDay, GetDayEndHistory), `Features/Denominations/*`; `IBusinessDayLock` used by bill and payment commands |
| Infrastructure | EF configuration, migration `Phase13_CashAndDayEnd`, seed denominations and permissions |
| Api | `ShiftsController`, `DayEndController`, `DenominationsController` (thin, `Send`) |
| Contracts | DTOs below, `SHIFT_REQUIRED`, `SHIFT_ALREADY_OPEN`, `DAY_CLOSED`, `VARIANCE_APPROVAL_REQUIRED` error codes, events |
| Desktop | Shift bar on billing terminals, `DenominationSheet` control, open/close/handover dialogs, X/Z preview and print, Day End wizard, Day End history, denominations admin (hub tab under Billing setup) |
| Tests | Domain (expected cash, state machine), handler tests per command, API contract + authorization matrix update, desktop VM tests, rendering screens |

## 5. Data model

Migration `Phase13_CashAndDayEnd`.

- `Denominations` (Id, Value, Label, Kind Note|Coin, SortOrder, IsActive).
- `CashShifts` (Id, DeviceId, BusinessDay, CashierUserId, Status, OpenedAt/By, FloatAmount, ClosedAt/By,
  ExpectedCash, CountedCash, Variance, VarianceReason, VarianceApprovedBy, BlindCount, HandedOverToShiftId,
  ForcedReason, RowVersion). A unique filtered index allows only one Open shift per device.
- `CashShiftCounts` (ShiftId, Phase Open|Close, DenominationId, Quantity).
- `CashMovements` (Id, ShiftId, Type PayIn|PayOut|TillVoucher, Amount, Reason, ApprovedBy, CreatedAt/By).
- `Payments.CashShiftId` (nullable FK; set for cash payments and cash refunds).
- `NonTillCollections` (Id, BusinessDay, Method, Amount, Reference, Note, RecordedBy/At).
- `BusinessDayCloses` (BusinessDay PK, Status Closed|Reopened, ClosedAt/By, Overrides JSON, TotalsSnapshot JSON,
  BackupFile, ReopenedAt/By, ReopenReason).

## 6. API endpoints

| Method | Route | Permission |
|---|---|---|
| GET | `/api/denominations` | `billing.operate` |
| PUT | `/api/denominations` | `settings.manage` |
| POST | `/api/shifts` (open on this device) | `shift.operate` |
| GET | `/api/shifts/current` (this device) | `shift.operate` |
| POST | `/api/shifts/{id}/movements` | `shift.operate` (pay-out above limit: approval) |
| GET | `/api/shifts/{id}/x-report` | `shift.operate` |
| POST | `/api/shifts/{id}/close` | `shift.operate` (+ approval over the variance limit) |
| POST | `/api/shifts/{id}/handover` | `shift.operate` |
| POST | `/api/shifts/{id}/force-close` | `shift.manage` |
| POST | `/api/shifts/{id}/reopen` | `shift.manage` + approval |
| GET | `/api/shifts?day=` | `shift.manage` |
| GET | `/api/day-end/precheck?day=` | `dayend.run` |
| POST | `/api/day-end` | `dayend.run` |
| POST | `/api/day-end/{day}/reopen` | `dayend.run` + approval |
| GET | `/api/day-end/history` | `dayend.run` |
| POST | `/api/day-end/{day}/collections` | `dayend.run` |

New permissions `shift.operate` (Cashier, Manager, Admin), `shift.manage` and `dayend.run` (Manager, Admin). The
authorization matrix snapshot is updated deliberately in this phase.

## 7. Real-time events

`ShiftChanged` (opened/closed/handed over; to managers and that device), `BusinessDayClosed` and `BusinessDayReopened`
(all): billing terminals refresh, and the shift bar shows that a new business day has started.

## 8. Business rules

- Expected cash = float + cash payments − cash refunds + pay-ins − pay-outs − till vouchers, for that shift only.
- One open shift per billing terminal; a cashier may hold a shift on one terminal at a time.
- A cash payment or refund records `CashShiftId` in the same transaction. No open shift: `SHIFT_REQUIRED`, and the
  desktop offers "Open shift".
- Closing: counted = Σ(denomination × quantity); variance = counted − expected. A variance over the limit needs a
  manager approval token. A reason is required for any non-zero variance.
- A closed business day: creating, finalizing, paying, refunding, voiding or reopening a bill dated in it returns
  `DAY_CLOSED`. Enforced in the command pipeline by `IBusinessDayLock`, which Phase 15's month lock reuses.
- Day End blocking items (open orders, unpaid bills, open shifts, open kitchen tickets) each need an override with a
  reason. Overrides are stored in `BusinessDayCloses.Overrides` and audited.
- Day End takes the backup **first**. If the backup fails or does not verify, the day is not closed (BillingHub's
  vault-first rule).
- Default business day: the current one by `BusinessDayStartTime`; just after the start time, a day that traded and
  is not closed yet is offered as the default.
- Everything is audited; amounts are `decimal(12,2)`; no floating point.

## 9. Desktop screens

- **Shift bar** (billing terminals, under the top bar): "Shift open since 09:02 · Priya · expected ₹4,820" with X
  report, pay in/out and close; "No shift open — Open shift" when closed.
- **Denomination sheet** (open, close, handover): one row per note and coin, quantity stepper (touch friendly), live
  total, expected/variance (hidden until counted for a blind count).
- **X / Z report** preview and print (Phase 7 renderer; Phase 14 template later).
- **Day End wizard** (managers): 1 checks (each blocking item with "Open" and "Override…"), 2 summary (sales,
  payments by method, non-till collections, shifts and variances), 3 run (backup → lock → pack) with progress, 4 done
  (files and history link).
- **Shifts** and **Day End history** screens (managers); **Denominations** tab in Billing setup.

## 10. Tests

- Domain: expected cash with refunds and movements; shift state machine; denomination totals.
- Application: open twice → `SHIFT_ALREADY_OPEN`; cash payment without shift → `SHIFT_REQUIRED`, card → OK; close
  with variance over the limit without approval → `VARIANCE_APPROVAL_REQUIRED`; handover carries the counted cash;
  Day End refuses on a failed backup and leaves the day open; closed day refuses bills and payments (`DAY_CLOSED`);
  reopen + rerun.
- API: endpoints, permissions (matrix), events. Desktop: shift bar, denomination sheet, wizard VMs; rendering
  screens for each.
- Concurrency: two terminals paying at once each attach to their own shift; Day End racing a payment either closes
  after it or refuses it.

## 11. Manual demo script

1. Billing terminal A: open a shift with ₹2,000 float; terminal B: open a shift with ₹1,000.
2. Settle bills on both with cash, card and UPI; refund one cash payment on A; pay out ₹200 on B (reason).
3. X report on A; close A counting ₹50 short (reason); close B over the limit, so a manager approves.
4. Manager: Day End shows an open table as blocking; settle it; run Day End; a backup file appears and the history
   shows totals.
5. Try to bill on the closed day: refused. The manager reopens with a reason, the bill is added, Day End reruns.

## 12. Acceptance criteria

- [ ] A full day with two counters reconciles to the rupee (expected = counted ± explained variance).
- [x] Cash cannot be taken without an open shift; Day End locks the day and reopening is audited.
- [x] Day End never closes a day without a verified backup.
- [ ] Definition of Done satisfied.

## 13. Risks and notes

- Locking a closed day touches every bill and payment command: keep it in one pipeline behaviour, not per handler.
- Shift and day boundaries around midnight: always use the business day, never the calendar date.
- Training: cashiers must open a shift before cash; the shift bar makes the state obvious, and `SHIFT_REQUIRED` says
  what to do.

## 14. Changes during implementation

**Built (2026-10-14, branch `phase-13-shifts-cash-day-end`)**

- Domain `Domain/Cash`: `CashShift` (+ `CashShiftCount`, `CashMovement`), `Denomination`, `DenominationCount`,
  `ExpectedCash`, `NonTillCollection`, `BusinessDayClose`; `Domain/Common/BusinessDay` (also used by
  `BusinessDayCalculator`). `Payment.CashShiftId`. Migration `Phase13_CashAndDayEnd` (also grants the new permissions
  to existing roles); `DbSeeder` seeds Indian notes/coins and the settings `CashVarianceApprovalLimit` (100),
  `CashPayOutApprovalLimit` (1000), `CashBlindCount` (false).
- Application: `Features/Shifts` (8 commands/queries + `ListCashiers`), `Features/DayEnd` (precheck, run, internal
  `CloseBusinessDay` / `RecordDayEndPack`, reopen, history, non-till collections), `Features/Denominations`.
  `BusinessDayLockBehavior` (after `TransactionBehavior`) applies `IBusinessDayLock` to every `IBusinessDayGuarded`
  command: RequestBill, FinalizeBill, AddPayment, RefundPayment, VoidBill, ReopenBill, OpenShift, AddCashMovement,
  HandOverShift, AddNonTillCollection.
- Api: `ShiftsController`, `DayEndController`, `DenominationsController`; 4 error codes → 409.
- Desktop: shift bar + dialogs (`Modules/Cash`), counting sheet, Cash Shifts, Day End wizard, Day End History,
  Denominations tab in Billing setup; X/Z print via `ShiftReportLayout` (one switch arm in `EscPosRenderer`) on the
  receipt printer profile.
- Authorization matrix: 17 added lines (no other change): `api/day-end` (POST), `api/day-end/history`,
  `api/day-end/precheck`, `api/day-end/{day}/collections`, `api/day-end/{day}/reopen` (AM---);
  `api/denominations` GET (AM--C), PUT (A----); `api/shifts` GET (AM---), POST (AM--C); `api/shifts/cashiers`,
  `api/shifts/current`, `api/shifts/{id}/close`, `/handover`, `/movements`, `/x-report` (AM--C);
  `api/shifts/{id}/force-close`, `/reopen` (AM---).

**Decisions and deviations**

- "A bill dated in a closed day" = today's business day plus the day the bill was settled in (sales count by
  `SettledAt`), so a carried-over unpaid bill can still be paid the next day, but refunding a closed day's bill is
  refused until the day is reopened.
- Concurrency: per-day PostgreSQL advisory locks (shared for bill commands, exclusive for Day End close/reopen) so
  Day End either waits for a payment in flight or the payment waits and gets `DAY_CLOSED`; cash payments share-lock
  the shift row and close takes it `FOR UPDATE`, so expected cash includes every committed payment. Raw SQL lives in
  Infrastructure (`BusinessDayLock`, `ShiftLocks`).
- `RunDayEndCommand` is a plain MediatR request like `RunBackupCommand` (no transaction around a minutes-long
  `pg_dump`); the close is its own audited command and re-checks the blocking items under the lock. If the pack
  cannot be written the day stays closed with a warning. New `BackupTrigger.DayEnd`.
- Pack = the existing reports + shifts + non-till collections as CSV in `dayend-<day>-<time>` next to the backups
  (Phase 14 adds PDF/Excel). Backup retention still applies to Day End backups.
- `CashShifts.DeviceId` has no FK (like `Payments.DeviceId`). `CashShiftCounts.Value` snapshots the note value;
  `BusinessDayCloses.PackFolder` added. One open shift per cashier is also a unique index.
- Approval actions `cash-variance`, `pay-out`, `reopen-shift`, `reopen-day`; as in Phase 9 a caller holding the
  action's permission approves their own (managers reopening a day or shift give a reason, audited).
- Blind count hides expected cash and cash takings from cashiers (not managers) until the shift is closed.
- Default Day End day: the previous day when it traded, is not closed and today has not traded yet.
- Existing tests that take cash open a shift in their arrange step; the API two-counter race now uses manager1 on
  the second counter (a cashier holds one shift at a time). Assertions unchanged.
- Not done: manual two-machine demo (§ 11); idempotency keys on cash movements.
