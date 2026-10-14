# Phase 13 — Shifts, Cash Register and Day End

Status: Not started · Depends on: Phase 12 · Reference: BillingHub `Features/CashRegister`, `RunDayEndCommand`,
`DayCloseGuard`, `DenominationSheetControl`

## 1. Goal

Account for every rupee at each billing counter: a cashier opens a shift with a float, cash movements during the
shift are recorded, the shift closes with a denomination count against the expected amount (short/excess), and the
manager closes the business day (Day End) with a report pack after which that day accepts no more bills.

## 3. Scope

**In**
- Shift per billing terminal and cashier: open (float, denominations), pay-in / pay-out / till vouchers with reason,
  X report (mid-shift, no reset), close with counted denominations, expected vs counted, short/excess with reason,
  Z report. Cash payments need an open shift on that terminal; card/UPI do not.
- Manager approval for short/excess above a threshold (setting) and for reopening a closed shift.
- Day End: pre-checks (no open orders or unpaid bills for the day, all shifts closed — with explicit override and
  reason), summary and PDF/Excel pack (Phase 14), business day locked: no new or changed bills dated in it.
- Owner/UPI collections recorded outside the till.

**Out**
- Bank reconciliation, petty-cash accounting beyond till vouchers.

## 5. Data model

`CashShifts` (terminal, cashier, opened/closed at, float, expected, counted, difference, status, approvals),
`CashShiftDenominations`, `CashMovements` (type, amount, reason, approver), `BusinessDayCloses` (business day,
closed by/at, totals snapshot, overrides). Migration `Phase13_CashAndDayEnd`.

## 6. API endpoints

`/api/shifts` (open, current, movements, x-report, close, reopen), `/api/day-end` (check, run, history).
Permissions `shift.operate`, `shift.manage`, `dayend.run`.

## 7. Real-time events

`ShiftChanged` (managers, that terminal), `BusinessDayClosed` (all).

## 8. Business rules

- Expected cash = float + cash payments − cash refunds + pay-ins − pay-outs.
- A closed business day is read-only for bills and payments (period lock, same mechanism as Phase 15).
- Everything audited.

## 9. Desktop screens

Shift bar on billing terminals (open/close, current expected), denomination sheet, X/Z report preview and print,
Day End wizard (checks → summary → close).

## 12. Acceptance criteria

- [ ] A full day with two counters reconciles to the rupee; Day End locks the day.
- [ ] Definition of Done satisfied.

## 14. Changes during implementation

(fill in while building)
