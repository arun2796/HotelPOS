# Phase 22 · B2 — Item-level bill split (specification)

Status: **Closed — owner decided on 2026-10-19 that item-level split is not needed** (amount-wise split covers the
venue). Nothing is implemented; the design is kept on file in case the need arises. B3/B4 would start only after a
renewed approval of § 6. Parent: `phase-22-excluded-features.md`
decision E5, steps B2–B4.

## 1. Question for the owner first

Amount-wise splitting already exists: a bill can be paid in several partial payments by different methods, and the
cashier can type any amount per payment. Item-level split only adds value when guests at one table want **separate
invoices** each listing their own items. If the venue does not do that, skip B3–B4 entirely (8.5 agent-days saved).

## 2. What exists today (facts the design must respect)

| Fact | Where |
|---|---|
| One live bill per order: unique index `IX_Bills_OrderId_Live` on `OrderId` filtered `Status <> Voided` | `BillingConfigurations.cs` |
| A bill is `Open → Finalized → Settled`, or `Voided`. Only `Open` bills can be edited (discount, customer, items are fixed at creation). | `Bill.cs` |
| `Finalize` assigns the invoice number from `IInvoiceNumberService`, stamps GST, marks the order Billed. A zero bill settles at once. | `BillWorkflow.FinalizeAsync` |
| Settling marks the order Paid, closes it when `AutoCloseOnFullPayment`, closes open kitchen tickets, detaches the table. | `BillWorkflow.SettleAsync` |
| `Void` (finalized, unpaid) cancels the order and frees the table; `Reopen` (open, unpaid) returns the order to the waiter. Both refuse when `PaidAmount > 0` ("refund instead"). | `VoidBill.cs`, `ReopenBill.cs`, `Bill.CancelUnpaid` |
| Totals: `GrandTotal = Subtotal − Discount + Tax + RoundOff`; the discount is allocated to lines pro rata by `LineSubtotal` with the rounding remainder on the last non-zero line; tax is per line on `(LineSubtotal − DiscountShare)`. | `BillCalculator.cs` |
| Reports count bills (`COUNT(*)`), covers from `Orders.GuestCount` via `JOIN Orders`, and GST registers list one row per invoice number; a settled bill without an invoice number is a GST error. | `ReportQueries.cs`, `GstData.cs` |
| Receipts and KOT print the bill number / invoice number; the cashier queue shows one card per bill. | `PrintDocuments.cs`, `BillingQueueViewModel.cs` |

## 3. Design

### 3.1 Model

- `Bills.SplitIndex int NOT NULL DEFAULT 0`. The bill created by `RequestBill` is index 0; siblings are 1, 2, …
  Siblings share `OrderId`, `TableCode`, `WaiterId`, customer defaults (copied, editable per sibling) and the
  **original** bill's discount definition (see 3.3).
- Index change (migration `Phase22_BillSplit`): drop `IX_Bills_OrderId_Live`; add unique `(OrderId, SplitIndex)`
  filtered `Status <> Voided`. A reopened order still gets a fresh bill because its previous bill is Voided.
- `BillItems` rows move between siblings (`BillId` changes); a partially moved quantity becomes two rows that share
  the same `OrderItemId` (the FK allows it). No new table.
- Additive DTO fields: `BillDetailDto.SplitIndex`, `BillDetailDto.SiblingBillIds[]`, `BillQueueItemDto.SplitIndex`,
  `BillQueueItemDto.SplitCount`. Display label: `#1016` for an unsplit bill, `#1016-A`, `#1016-B`, … for siblings
  (A = index 0 once a split exists). Printed receipts carry the same suffix next to the bill number; the **invoice
  number is unaffected** (each sibling gets its own from the normal sequence at Finalize).

### 3.2 Command

`POST api/bills/{id}/split` → `SplitBillRequest { Items: [{ BillItemId, Quantity }], RowVersion }` → returns the
new sibling `BillDetailDto`. Permission: `billing.operate` plus the same claim rule as every bill edit (the bill
must be claimed by the caller's terminal, `Bill.IsHeldByOther`).

Preconditions (all return `InvalidStateTransition` / `ValidationError`, never partial work):

1. Source bill `Status == Open`, `PaidAmount == 0`, not finalized. **Splitting after Finalize is forbidden** (the
   invoice number is already issued); the cashier must Reopen → waiter re-requests → split.
2. At least one item with `1 ≤ Quantity ≤ BillItem.Quantity`; at least one unit must **remain** on the source
   (a split that empties the source is rejected — use "move all" = no split).
3. A bill may be split repeatedly (A → A+B, A → A+C …) and a sibling may itself be split. Maximum 9 live siblings
   per order (labels A–J; more is a usability smell, not a real need).
4. The order must still be in `Billed`/`BillRequested` state (same guard as other bill edits).

Effects: new `Bill` with `SplitIndex = max + 1`, `Status Open`, moved/cloned `BillItem`s, discount re-applied (3.3),
both bills recalculated with the venue's `RoundOff` setting, audit `BillSplit` with `{ From, To, Items }`, realtime
`BillUpdated(Finalized?no → Split)` for both bills so queue cards refresh, `OrderUpdated(Billed)` unchanged.

### 3.3 Discount, tax and rounding (the part that must be exact)

- **Discount.** The source bill's discount (`DiscountId/Type/Value/Reason/ApprovedBy`) is copied to the sibling and
  **recomputed per bill** by `BillCalculator` on that bill's own subtotal: a 10 % discount stays 10 % on each; a
  fixed ₹100 discount is **split pro rata by subtotal** between the two bills at split time (`Money(100 × subA / (subA+subB))`,
  remainder to the source), and each bill then carries its own `FixedAmount` value. Sum of the two discounts equals
  the original to the paise; a manager's approval on the source counts for both (recorded in the audit, no re-approval).
- **Tax.** Per line, unchanged: `tax = Money(taxable × rate / 100)`. Because tax is already computed per line, the sum
  of sibling taxes differs from the pre-split tax only by line-level rounding **when a line is quantity-split**
  (one 3-unit line becomes 2 + 1). Accepted: each sibling is a legally separate invoice, and the GST register sums
  invoices, not orders. Reports reconcile at the invoice level by definition.
- **Round-off.** Each sibling rounds independently (the venue setting). The order-level total may therefore move by up
  to ±0.50 × siblings. Shown on the receipt as usual; the acceptance test asserts `Σ sibling GrandTotal` equals the
  unsplit `GrandTotal` ± `0.50 × (siblings − 1)` and equals it exactly when round-off is off.
- **Service charge** (if `ServiceChargeAmount` is in use): same rule as a percentage discount — recomputed per bill.

### 3.4 Lifecycle with siblings

| Event | Rule |
|---|---|
| Finalize sibling | Normal: own invoice number, GST stamp. `order.MarkBilled()` is idempotent. |
| Settle sibling | `BillWorkflow.SettleAsync` runs **only the bill-local parts** (payments, audit, `PaymentCompleted`). The order-level parts (`MarkPaid`, `Close`, close kitchen tickets, detach the table) run **only when every live sibling of the order is Settled**. Until then the table stays occupied and the queue shows the remaining siblings. |
| Reopen one sibling (open, unpaid) | Allowed only while **no sibling has been finalized or paid**; otherwise rejected ("settle or void the other parts first"). Reopening when siblings exist **merges**: the reopened sibling's items return to the lowest-index live sibling, the sibling is Voided (reason "Merged back"), and if that was the last sibling the order itself reopens as today. |
| Void one sibling (finalized, unpaid) | Allowed. Items go back to the lowest-index live sibling (merge), which is recalculated; the voided sibling keeps its invoice number in the Voided list (GST cancelled-invoice report already handles voided invoice numbers). The order is cancelled only when the **last** live sibling is voided (today's behaviour). |
| Refund on a settled sibling | Unchanged (bill-local). |
| Waiter adds items after a split | `RequestBill`/re-request today creates one bill from all order items. With siblings present, new order items are appended to the **lowest-index Open sibling**; if none is Open the request is rejected ("all parts of this bill are finalized — void one to add items"). |
| Cancel order | Only possible when every live sibling is `Open` and unpaid (same guard as today, applied to all). |

### 3.5 UI (B4)

- Cashier workspace (desktop): "Split items" button on an Open, claimed bill opens a picker listing each line with a
  quantity stepper (default 0) and a running total of the new part; Confirm calls the command, the new sibling
  appears as a separate queue card `#1016-B` under the same table, selected. Siblings are grouped visually (same
  table chip, a "2 of 3 parts" caption). Merge-back happens through the existing Void / Reopen buttons with the
  rules above (confirmation text explains the merge).
- Receipts/PDF: label suffix after the bill number; "Part B of 3" in the footer. Invoice number unchanged.
- Web (waiter) order details: lists sibling bills with status chips; no split action on the web.
- Reports: no new columns. `Bills` counts rise with splits (each is an invoice); `Covers` would double-count
  `GuestCount` per sibling, so the daily/hourly SQL changes to `SUM(GuestCount)` over **distinct orders**
  (`COUNT(DISTINCT o.Id)`-style subquery). Reconciliation test updated accordingly.

### 3.6 Out of scope

Splitting by seat/guest number (orders have no seats), moving items between **different** orders, splitting after
Finalize, re-merging settled bills, equal-share "split evenly" (that is the existing amount split).

## 4. Acceptance tests (written before code)

1. 3 lines (qty 2, 1, 3), 10 % discount, GST 5 %, round-off on: split qty 1 of line 1 + line 2 → two bills; discounts
   sum to the original; each bill's tax equals its own line sums; `Σ GrandTotal` within ±0.50 of the original.
2. Fixed ₹100 discount pro rata: shares sum to exactly 100.00.
3. Split → finalize both → settle A: order still Billed/table occupied; settle B: order Paid/Closed, table free,
   kitchen tickets closed once.
4. Split → void B before paying: items return to A, A recalculated, B Voided with its invoice number listed in the
   GST cancelled report; order not cancelled.
5. Reject: split a finalized bill; split emptying the source; split with paid amount; 10th sibling; sibling of
   another terminal's claimed bill.
6. GST register: two invoice numbers, each with correct taxable/tax; `InvoiceNumberMissing` never raised.
7. Daily sales: `Bills` = number of invoices, `Covers` = `GuestCount` once per order.
8. Authorization matrix row for `POST api/bills/{id:int}/split`.

## 5. Effort

B3 backend 3 days + 2 tests, B4 UI 2 desktop + 1 web + 0.5 tests (plan § 5). Migration `Phase22_BillSplit` only
changes an index and adds a defaulted column: safe on a live venue database.

## 6. Decision needed

- [ ] Owner confirms the venue splits by item (otherwise close B2–B4 as "not needed").
- [ ] Owner accepts: no split after Finalize; fixed discounts split pro rata; per-sibling round-off; max 9 parts.
