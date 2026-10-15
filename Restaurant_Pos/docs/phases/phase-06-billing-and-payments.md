# Phase 6 — Billing & Payments

Status: Done (2026-10-07) · Depends on: Phase 5

## 1. Goal

The waiter requests the bill; it appears in the cashier queue instantly. The cashier reviews lines,
applies a discount (with manager approval when required), finalises the invoice (gap-free number),
records one or several payments (Cash / Card / UPI / split), and the order closes and the table is
released — all in one server-side transaction. Several billing counters can work at once without
settling the same bill twice. Voids, refunds and reopening are manager-controlled and audited.

## 2. Prerequisites

- Phase 5. Calculation rules in `docs/02` § 6, bill/payment state machines in `docs/02` § 4.4,
  manager approval protocol in `docs/03` § 5.

## 3. Scope

**In**
- `Bill`, `BillItem`, `Payment`, `Discount`, `PaymentMethod` (admin CRUD for the last two).
- Request bill, claim/release, discount set/clear, customer details, finalize, payments (split),
  close, reopen, void, refund.
- `BillCalculator` in Domain (pure, fully unit-tested).
- Invoice numbering with gap-free counter.
- Events: `BillRequested`, `BillUpdated`, `PaymentCompleted`, `TableStatusChanged`, `OrderUpdated`.
- Cashier UI: Billing Queue, Bill Detail, Payment, Closed Bills; waiter **REQUEST BILL**; Admin
  Discounts / Payment Methods.

**Out**
- Printing (Phase 7) — this phase shows an on-screen invoice/receipt preview only.
- Card terminal integration, UPI QR generation (reference entered manually), tips/service charge
  (deferred; add as a bill-level line later), tax-inclusive prices.

## 4. Deliverables by project

| Project | Deliverables |
|---|---|
| Contracts | `BillSummaryDto`, `BillDetailDto` (lines, totals, tax breakup, payments, claim info, customer), `PaymentDto`, `ApplyDiscountRequest`, `UpdateCustomerRequest`, `AddPaymentRequest`, `RefundRequest`, `VoidBillRequest`, `ReopenBillRequest`, `ManagerApprovalDto`, `DiscountDto` + requests, `PaymentMethodDto` + requests, events `BillRequested`, `BillUpdated`, `PaymentCompleted` |
| Domain | `Bill` (+ state guards, `Claim/Release`, `ApplyDiscount`, `Finalize`, `AddPayment`, `Void`, `Reopen`), `BillItem`, `Payment`, `Discount`, `PaymentMethod`, `BillCalculator`, `InvoiceNumberFormatter` |
| Application | `IBillingService` (`RequestBillAsync`, `ListPendingAsync`, `GetAsync`, `ListClosedAsync`, `ClaimAsync`, `ReleaseAsync`, `SetDiscountAsync`, `ClearDiscountAsync`, `SetCustomerAsync`, `FinalizeAsync`, `AddPaymentAsync`, `CloseAsync`, `ReopenAsync`, `VoidAsync`, `RefundAsync`), `IManagerApprovalService`, `IInvoiceNumberService` (locked counter), `IDiscountService`, `IPaymentMethodService`, validators |
| Infrastructure | configurations, `Phase06_Billing` migration (tables, `BillNumbers` sequence, invoice counter rows in `Settings`), claim-expiry cleanup |
| Api | `BillingController`, `DiscountsController`, `PaymentMethodsController`, `OrdersController.RequestBill`, events |
| Desktop | Cashier: `BillingQueueView/VM`, `BillDetailView/VM`, `PaymentView/VM` (cash NumPad, quick tender, split lines), `ClosedBillsView/VM`, `ManagerApprovalDialog`, invoice/receipt preview; Waiter: **REQUEST BILL** + bill status in Table Details; Admin: Discounts, Payment Methods; keyboard shortcuts |
| Tests | see § 10 |

## 5. Data model

`docs/02` § 2.6. Migration `Phase06_Billing`.

## 6. API endpoints

`docs/03-api-reference.md` § 3.7 and `request-bill` in § 3.5. `[I]`: request-bill, payments,
refunds.

## 7. Real-time events

| Event | Audience | When |
|---|---|---|
| `BillRequested` | cashiers, managers | bill created |
| `BillUpdated` | cashiers, managers, owning waiter | claim/release/discount/customer/finalize/partial payment/reopen/void |
| `PaymentCompleted` | cashiers, waiters, managers, admins | bill settled |
| `TableStatusChanged` | all | Billing on request; Available on settle; back to Occupied on reopen |
| `OrderUpdated` | kitchen (to drop any lingering tickets), waiter | BillRequested / Billed / Paid / Completed |

## 8. Business rules

- Request bill: order must be in `Served` or `Ready` (all tickets at least Ready; setting
  `AllowBillBeforeReady` for drinks-only cases, default false). Creates the bill with snapshotted
  lines and computed totals, order -> `BillRequested`, table -> `Billing`. Appending items is now
  rejected (`ORDER_LOCKED`).
- Claim: soft lock for 5 minutes (`ClaimExpiresAt`), refreshed by any cashier action; another
  device gets 409 `BILL_CLAIMED` with holder name; a manager can override (audited). Expired claims
  are ignored.
- Discount: either a predefined `Discount` or manual (type, value, reason). Predefined with
  `RequiresApproval` or manual discounts above `MaxCashierDiscountPercent` (setting, default 10)
  require manager approval. Recalculates totals; not allowed after finalize (reopen first).
- Finalize: assigns the invoice number (gap-free, inside the transaction with `SELECT ... FOR UPDATE` on the counter
  row), freezes lines/discount, order -> `Billed`. Explicit (print before payment) or implicit on the
  first payment.
- Payments: `Amount > 0`; non-cash amount must not exceed balance due (409
  `PAYMENT_EXCEEDS_BALANCE`); cash may have `Tendered >= Amount` with change computed; `Reference`
  required when the method says so. Each payment is idempotent on `Idempotency-Key`. When
  `PaidAmount >= GrandTotal`: `PaymentStatus = Paid`, `Bill.Status = Settled`, order -> `Paid` ->
  `Completed` (when `AutoCloseOnFullPayment`), table -> `Available`, open tickets -> `Completed`,
  all inside one transaction, then events.
- Reopen (cashier/manager, unpaid bills only): bill `Voided`, order -> `Served`, table -> `Occupied`,
  items editable again; a new bill gets a new bill number and, when finalised, a new invoice number;
  the voided invoice number is recorded in audit (cancelled invoice).
- Void (manager): finalized unpaid bill -> `Voided`, order -> `Cancelled`, table released.
- Refund (manager): against a specific payment, amount ≤ payment amount, creates a negative
  `Payment` row with `RefundOfPaymentId`; full refund of all payments sets `PaymentStatus =
  Refunded`. Order/table are not reopened.
- Concurrency: every mutating call checks `RowVersion`; two simultaneous payments on one bill ->
  one succeeds, the other gets 409 and refreshes.
- Audit: `Bill.Requested`, `Bill.Claimed/Released/ClaimOverridden`, `Bill.DiscountApplied/Cleared`
  (old/new), `Bill.Finalized`, `Payment.Received`, `Bill.Settled`, `Bill.Reopened`, `Bill.Voided`,
  `Payment.Refunded`.

## 9. Desktop screens

| Screen | Behaviour |
|---|---|
| Billing Queue | Cards/rows: table, order #, waiter, requested time, total, claim badge; sorted oldest first; live via events; **OPEN** claims the bill |
| Bill Detail | Lines, subtotal, discount row with **DISCOUNT** button (list of predefined + manual; approval dialog when required), tax breakup (CGST/SGST), round-off, grand total (large); customer button; **FINALIZE & PRINT** (preview in this phase); payment buttons **CASH / CARD / UPI / SPLIT**; **REOPEN** |
| Payment | Cash: amount, tendered NumPad, quick buttons, change; Card/UPI: amount + reference; Split: list of method/amount lines with remaining balance; **CONFIRM** disabled until valid; result screen with change due and receipt preview |
| Closed Bills | Today (date picker), search by table/invoice/order; actions: view, reprint (Phase 7), refund, void (manager approval dialog) |
| Waiter Table Details | **REQUEST BILL** (when allowed); shows "Bill at counter" / "Paid" |
| Admin | Discounts (name, type, value, requires approval, active); Payment Methods (name, code, requires reference, active, order) |

## 10. Tests

- Domain `BillCalculator`: table-driven cases — no discount; 10 % discount with rounding remainder
  on last line; fixed discount larger than subtotal capped; mixed tax rates; zero-tax items; round-off
  on/off; modifiers included in line subtotal; cancelled items excluded.
- Domain `Bill`: state guards (discount after finalize rejected; payment on voided rejected; refund
  exceeding payment rejected).
- Application: request bill snapshots lines and blocks append; claim conflict and expiry; discount
  approval required/accepted/invalid; finalize assigns sequential invoice numbers under 20 parallel
  finalisations (no gaps/duplicates); split payment settles exactly at total; non-cash overpayment
  rejected; cash change computed; idempotent payment (same key twice -> one payment); concurrent
  payments -> one 409; full-payment transaction updates bill, order, table, tickets, audit together;
  injected failure after payment insert rolls everything back; reopen/void/refund role rules.
- Api: cashier cannot access `/api/menu-items` writes; kitchen cannot call billing endpoints;
  `BillRequested` reaches cashier hub client; `PaymentCompleted` reaches waiter; two
  `WebApplicationFactory` clients settling the same bill -> one success.
- Desktop: `PaymentViewModel` split validation; `BillingQueueViewModel` shows claim badge and
  removes settled bills on `PaymentCompleted`.

## 11. Manual demo script

1. Waiter requests bill for T05 -> BILLING-01 queue shows it within 1 s; table turns Billing.
2. BILLING-01 opens it (claim); BILLING-02 sees "Being handled by BILLING-01" and gets a conflict on
   open.
3. Apply 15 % discount -> approval dialog -> manager enters credentials -> totals recalculated.
4. Finalize: invoice number assigned (e.g. INV-202610-000001); preview shown.
5. Split payment: Cash 400 (tendered 500, change 100) + UPI 356 -> settled; T05 Available on waiter
   screen; order Completed.
6. Unplug BILLING-01 during CONFIRM: retry with same key -> exactly one payment row.
7. Closed Bills: refund 100 with manager approval; audit shows all steps with user, device, old/new
   values.
8. Reopen an unpaid bill, append an item on the waiter side, request bill again -> new bill, new
   invoice number on finalize; audit shows the cancelled invoice.

## 12. Acceptance criteria

- [x] Request -> queue -> discount -> finalize -> split payment -> close -> table released, atomic.
- [x] Gap-free invoice numbers under concurrency; idempotent payments; multi-counter claim lock.
- [x] Void, refund, reopen with manager approval and audit.
- [x] Calculation rules match `docs/02` § 6 exactly (tests are the spec).
- [x] Definition of Done satisfied (the two-PC unplug demo, step 6, is still to do by hand).

## 13. Risks and notes

- Keep `BillCalculator` free of EF/DI so tests are instant and the same code can later run in a
  preview on the client if ever needed.
- Legal invoice requirements vary (GSTIN, HSN codes); keep invoice fields in `Settings` and the
  `InvoiceDocument` (Phase 7) so they can be adjusted without schema changes.
- Service charge / tips are a frequent follow-up request; reserve `Bill.ServiceChargeAmount` in the
  schema now (nullable, unused) to avoid a later migration.

## 14. Changes during implementation

- **Invoice counter table.** Counters live in `InvoiceCounters (Period, LastNumber)` instead of `Settings` rows, so
  they never show up (or get edited) on the Settings screen. The number is taken with
  `INSERT ... ON CONFLICT DO UPDATE ... RETURNING`, which locks the period row until the finalisation commits, the
  same guarantee as `SELECT ... FOR UPDATE`. A rollback hands the number back (tested with an injected failure and
  20 parallel finalisations).
- **One live bill per order.** `Bills.OrderId` is unique only among non-voided bills, because a reopened order gets a
  new bill (new bill number, and a new invoice number on finalisation).
- **Refund rows** are negative `Payments` rows; the check constraint is `Amount > 0` for payments and `< 0` for
  refunds. `Bills.RefundedAmount` keeps the running total; `PaidAmount` stays the gross amount received.
- **Cash over the balance** is accepted: the applied amount is capped at the balance and the rest is change.
  Card/UPI above the balance -> 409 `PAYMENT_EXCEEDS_BALANCE`.
- **Approvals.** Void and refund accept cashiers too, but then require a manager's credentials in `approval`
  (the Closed Bills / Bill Detail approval dialog). Reopen needs approval only when the bill already has an invoice
  number (that invoice is cancelled). Rejected approvals are audited as `Approval.Rejected` (password never logged).
- **Claims.** Every cashier action claims or refreshes the claim; the Bill Detail screen renews it every two minutes
  while open, and a cleanup job releases expired claims every minute (emitting `BillUpdated`). A bill held by another
  counter opens read-only with "Being handled by BILLING-02"; a manager can take it over.
- **Settlement** closes the order (`Paid`, then `Completed` when `AutoCloseOnFullPayment`), releases the table and
  completes any ticket still open, in one transaction; `AutoCloseOnFullPayment = false` keeps the order `Paid` until
  `POST /close`, but the table is released at payment either way.
- A zero-total bill (100 % discount) is settled when finalized.
- New settings: `MaxCashierDiscountPercent` (10) and `AllowBillBeforeReady` (false).
- Desktop payments keep the idempotency key and the exact request only across connection failures; any definitive
  server answer (also a 4xx, which the server stores under the key) makes the next attempt use a new key.
- Printing remains Phase 7: FINALIZE & PRINT shows an on-screen invoice preview, and reprint is disabled.
- The waiter list used as a placeholder for Billing (`ActiveOrdersViewModel`) was removed.
