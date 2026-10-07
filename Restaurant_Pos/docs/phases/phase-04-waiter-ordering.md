# Phase 4 — Waiter Ordering

Status: Not started · Depends on: Phase 2, Phase 3

## 1. Goal

A waiter selects a table, builds an order (category -> item -> quantity -> notes/modifiers), saves a
draft or sends it to the kitchen, appends items later, and cancels when needed. Orders are created
exactly once even under network failures (idempotency), state transitions are enforced by the server,
and table status follows the order. The kitchen does not yet act on orders (Phase 5), but submitted
orders are visible to kitchen/cashier/manager screens as lists.

## 2. Prerequisites

- Phase 2 (tables), Phase 3 (menu), `OrderStatus` state machine in `docs/02` § 4.1,
  idempotency protocol in `docs/03` § 4.

## 3. Scope

**In**
- `Order`, `OrderItem`, `OrderItemModifier` with snapshots; order number sequence.
- Idempotency middleware + store (`IdempotencyRecords` table from Phase 1).
- Create draft / replace draft items / submit / append batch / update guests-notes / cancel.
- Table status coupling (Ordering, Preparing, Available on cancel).
- Events: `OrderCreated`, `OrderUpdated`, `OrderCancelled`, `TableStatusChanged`.
- Waiter UI: Table Details with order, Order Builder, My Orders; local draft persistence; network
  failure handling.
- Read-only "Active orders" list for Kitchen/Cashier/Manager (until Phase 5/6 replace it).

**Out**
- Kitchen tickets and status derivation (Phase 5) — in this phase `submit` sets `Submitted` and stops.
- Item cancellation after sending (Phase 5), bill request (Phase 6).

## 4. Deliverables by project

| Project | Deliverables |
|---|---|
| Contracts | `CreateOrderRequest` (`tableId, guestCount, notes, items[], submit`), `OrderItemInput` (`menuItemId, quantity, notes, modifierOptionIds[]`), `ReplaceOrderItemsRequest`, `AppendOrderItemsRequest`, `UpdateOrderRequest`, `CancelOrderRequest`, `OrderSummaryDto`, `OrderDetailDto`, `OrderItemDto`, `OrderItemModifierDto`, events `OrderCreated`, `OrderUpdated`, `OrderCancelled` |
| Domain | `Order` (+ `OrderStateMachine`: `CanTransition(from, to)`, `Submit()`, `AppendBatch()`, `Cancel(by role)`), `OrderItem`, `OrderItemModifier`, `Table.AttachOrder/DetachOrder` |
| Application | `IOrderService` (`CreateAsync`, `GetAsync`, `ListAsync`, `GetActiveAsync`, `ReplaceItemsAsync`, `SubmitAsync`, `AppendItemsAsync`, `UpdateAsync`, `CancelAsync`), `OrderItemFactory` (snapshots from menu, validates availability and modifier rules), `IIdempotencyStore`, validators |
| Infrastructure | configurations, `Phase04_Orders` migration (tables + `OrderNumbers` sequence + partial unique index), `IdempotencyStore`, cleanup hosted service (expired keys) |
| Api | `OrdersController`, `IdempotencyMiddleware` + `[Idempotent]` attribute, events |
| Desktop | Waiter: `TableDetailsViewModel` (order section), `OrderBuilderView/VM`, `CartItemViewModel`, `ModifierPickerView`, `MyOrdersView/VM`; `ILocalDraftStore`; `IOrderSubmitter` (idempotent retry); shared `ActiveOrdersView` for kitchen/cashier placeholders; Ready/notification plumbing prepared |
| Tests | see § 10 |

## 5. Data model

`docs/02` § 2.4. Migration `Phase04_Orders`: `Orders`, `OrderItems`, `OrderItemModifiers`,
sequence `"OrderNumbers"` starting 1001, partial unique index for one active order per table.

## 6. API endpoints

`docs/03-api-reference.md` § 3.5 rows for Phase 4: create, list, active, get, update, replace items,
submit, append items, cancel. `[I]` endpoints: create, append.

## 7. Real-time events

| Event | When |
|---|---|
| `OrderCreated` | submit (Draft -> Submitted) |
| `OrderUpdated` (`ItemsAppended`, `GuestsChanged`) | append / update |
| `OrderCancelled` | cancel |
| `TableStatusChanged` | draft created (Ordering), submitted (Preparing), cancelled (Available or Occupied) |

## 8. Business rules

- A table can have one active order. Creating an order on an `Available` or `Occupied` table attaches
  it; on `Ordering`/`Preparing`/… -> 409 `TABLE_NOT_AVAILABLE` with the existing order id so the client
  can open it instead.
- Items snapshot `ItemName`, `UnitPrice`, `TaxRatePercent`, `PreparationStationId`, modifier names
  and deltas at the moment they are added. Unavailable (sold out) or inactive items are rejected.
- Modifier selections must satisfy each group's min/max; options must belong to groups linked to the
  item.
- `submit` requires at least one item; sets `SubmittedAt`, batch 1 items -> `Sent`, table ->
  `Preparing`. In Phase 5 this also creates kitchen tickets inside the same transaction.
- `append items` allowed in band B statuses (Submitted…Served); creates batch n+1; rejected (409
  `ORDER_LOCKED`) once a bill exists.
- `replace items` only while `Draft`.
- Cancel rules: Draft/Submitted/Accepted by the owning waiter or a manager; Preparing/Ready by manager
  only with reason; Served and beyond cannot be cancelled (void bill instead). Cancel restores the
  table to `Occupied` if it was manually occupied before, else `Available`.
- Waiters see all orders but can modify only their own unless they are Manager
  (setting `AllowAnyWaiterToEditOrders`, default false).
- Order number from sequence; gaps acceptable.
- Idempotency on create/append as in `docs/01` § 6.3; the response of a replayed request is identical
  to the original.
- Audit: `Order.Submitted`, `Order.ItemsAppended`, `Order.Cancelled`, `Order.Updated`.

## 9. Desktop screens

| Screen | Behaviour |
|---|---|
| Table Details (extended) | Shows current order: number, status chip, waiter, items grouped by batch with status; **NEW ORDER** (when none), **OPEN ORDER**, **ADD ITEMS**, **CANCEL ORDER** (reason dialog) |
| Order Builder | Left: category list (large buttons). Centre: item grid (name, price, sold-out greyed). Right: cart with quantity stepper, note button (quick notes: "Less spicy", "No onion", free text), modifier sheet when the item has groups, remove. Footer: item count, provisional subtotal, **SAVE DRAFT**, **SEND TO KITCHEN**. In append mode the footer shows "Adding to order #1025". |
| My Orders | Logged-in waiter's active orders with status and elapsed time; tap opens Table Details |
| Local drafts | Draft saved on every cart change; reopening a table restores it; cleared after confirmed submit |
| Network failure | Submit disabled text changes to "Sending…"; on timeout -> automatic retry with same key; on failure -> dialog **Retry** / **Keep draft**; banner "Connection unavailable — the order was NOT sent" when disconnected |

## 10. Tests

- Domain: transition table test (every from/to pair expected allowed/forbidden, including
  Paid -> Preparing forbidden); `AppendBatch` increments batch number; cancel role rules.
- Application: create snapshots price even after menu price changes; unavailable item rejected;
  modifier min/max enforced; second active order on same table rejected; submit with empty draft
  rejected; append after bill (simulate `BillRequested`) rejected; idempotent create (same key twice ->
  same order, one row); same key different payload -> `IDEMPOTENCY_KEY_REUSED`; concurrent submit of
  same order -> one success, one 409; waiter cannot cancel another waiter's order; manager can.
- Api: full flow create -> submit -> append -> cancel with events asserted on a hub test client;
  `Idempotency-Key` missing -> 400; cancel of Preparing by waiter -> 403/409.
- Desktop: `OrderBuilderViewModel` persists draft on change; `OrderSubmitter` retries with the same
  key and reports "unconfirmed" after exhausting retries; cart totals computed for display only.

## 11. Manual demo script

1. Waiter opens T05, builds an order with modifiers and notes, saves draft, navigates away, returns:
   draft restored. Sends to kitchen: order #1001 appears in the kitchen/cashier active-orders list on
   another machine within 1 s; T05 shows Preparing.
2. Append "2 Coke" to #1001: batch 2 appears; table status unchanged.
3. **Network test**: open T06, add items, unplug the waiter PC's network, press Send: banner says the
   order was NOT sent and the draft remains. Plug in, press Send: exactly one order exists. Repeat
   with a simulated timeout (API paused via debugger) to see the retry use the same key and produce
   one order.
4. Try to create a second order on T05 from another waiter terminal: message shows the existing order.
5. Cancel #1001 as waiter while Submitted: table returns to Available; event visible on other screens.

## 12. Acceptance criteria

- [ ] Draft/submit/append/cancel flows with server-enforced transitions and audit.
- [ ] Idempotency middleware proven by tests and the network demo (no duplicate, no loss).
- [ ] Order builder usable with touch: table -> category -> item -> qty -> send without dialogs.
- [ ] Local drafts survive navigation and app restart.
- [ ] Definition of Done satisfied.

## 13. Risks and notes

- The request hash for idempotency must exclude volatile fields (client timestamps) — hash the
  canonical JSON of the DTO.
- Provisional totals on the client may differ from server totals by rounding; label them "approx."
  until the bill exists.
- Order-level status after append in band B will be recomputed in Phase 5; in this phase append
  leaves status `Submitted`.

## 14. Changes during implementation

(fill in while building)
