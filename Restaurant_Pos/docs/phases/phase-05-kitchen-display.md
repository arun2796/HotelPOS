# Phase 5 — Kitchen Display

Status: Done (2026-10-07) · Depends on: Phase 4

## 1. Goal

When a waiter sends an order, the server creates kitchen tickets (one per preparation station per
batch) and the kitchen display shows them instantly. Kitchen staff move tickets through
New -> Accepted -> Preparing -> Ready -> Completed with large touch buttons. Waiters are notified the
moment something is ready and mark it served. Order and table status are derived from ticket status.

## 2. Prerequisites

- Phase 4. Ticket state machine and order-status derivation in `docs/02` § 4.1–4.3.
- Station groups in SignalR (`docs/04` § 2).

## 3. Scope

**In**
- `KitchenOrder`, `KitchenOrderItem`; ticket creation on submit/append; routing by station.
- Kitchen actions: accept, start, ready, complete, recall; item cancellation after sending.
- Order status derivation; waiter serve action; table status Ready/Occupied.
- Events: `KitchenTicketCreated/Updated`, `OrderAccepted/Preparing/Ready/Served`,
  `TableStatusChanged`.
- Kitchen Display UI (three columns), Completed Orders, station selection from device config,
  elapsed-time colouring, sound; waiter "ORDER READY" notifications and per-ticket status in Table
  Details.
- Kitchen performance timestamps (used by Phase 8).

**Out**
- Printing KOTs (Phase 7), bump bar hardware, item-level ready states (deferred).

## 4. Deliverables by project

| Project | Deliverables |
|---|---|
| Contracts | `KitchenTicketDto` (ticket, table, waiter, order number, batch, station, status, created/elapsed, items with qty/notes/modifiers/cancelled flag), `KitchenTicketListDto`, `CancelOrderItemRequest`, events `KitchenTicketCreated`, `KitchenTicketUpdated`, `OrderAccepted`, `OrderPreparing`, `OrderReady`, `OrderServed`; `DeviceType.Kitchen` config includes `StationId` |
| Domain | `KitchenOrder` (+ `KitchenOrderStateMachine`), `KitchenOrderItem`, `Order.RecalculateKitchenStatus(tickets)`, `OrderItem.Cancel()` |
| Application | `IKitchenService` (`ListAsync(stationId,status)`, `GetAsync`, `ListCompletedAsync(date)`, `AcceptAsync`, `StartAsync`, `ReadyAsync`, `CompleteAsync`, `RecallAsync`), `TicketFactory` (group items by station), `OrderService.SubmitAsync/AppendItemsAsync` extended to create tickets in the same transaction, `OrderService.ServeAsync`, `OrderService.CancelItemAsync`, `OrderStatusDeriver` |
| Infrastructure | configurations, `Phase05_Kitchen` migration |
| Api | `KitchenController`, `OrdersController` additions (`serve`, `items/{id}/cancel`), hub `JoinStation/LeaveStation`, events |
| Desktop | Kitchen: `KitchenDisplayView/VM`, `TicketCard` control, `CompletedOrdersView/VM`, station picker in device settings, sound; Waiter: Ready banner/toast, per-ticket status in Table Details, **SERVED** button, item cancel; resync on reconnect |
| Tests | see § 10 |

## 5. Data model

`docs/02` § 2.5. Migration `Phase05_Kitchen`. `OrderItems.KitchenOrderId` populated on submit/append.

## 6. API endpoints

`docs/03-api-reference.md` § 3.6 (kitchen) and § 3.5 rows `serve`, `items/{itemId}/cancel`.

## 7. Real-time events

| Event | Audience | When |
|---|---|---|
| `KitchenTicketCreated` | `station:{id}`, `role:kitchen` | ticket created |
| `KitchenTicketUpdated` | station, kitchen, `user:{waiter}`, manager | any ticket transition or item cancel |
| `OrderAccepted` / `OrderPreparing` / `OrderReady` / `OrderServed` | waiter (user + role), cashier, manager | derived order status changes |
| `TableStatusChanged` | all | Preparing/Ready/Occupied transitions |

## 8. Business rules

- On submit/append: group the batch's items by `PreparationStationId`; one ticket per station;
  `TicketNumber = "{OrderNumber}-{Batch}"` (+ `-{StationCode}` when the batch spans several stations).
- Ticket transitions per `docs/02` § 4.2. `start` from `New` performs an implicit accept.
  `complete` allowed for Kitchen (bump) and Waiter (served). `recall` kitchen/manager only.
- After every ticket change, recompute order status (band B rules) and table status; emit the
  order-level event only when the derived status actually changed.
- `serve` (waiter) completes all `Ready` tickets of the order; if all tickets are completed the order
  becomes `Served` and the table `Occupied`.
- Item cancel after sending: allowed by waiter while the ticket is `New`/`Accepted`; by manager while
  `Preparing`; never when `Ready`/`Completed`. Cancelling the last open item cancels the ticket.
  Cancelled items stay visible on the ticket with strike-through until the ticket completes.
- Order cancel in band B cancels all open tickets (`KitchenTicketUpdated` with `Cancelled`).
- Elapsed-time thresholds from settings `KitchenWarnMinutes` / `KitchenLateMinutes` (returned by
  `/api/settings`).
- Kitchen screens with `StationId` set receive only their station; without it they see all stations.
- Audit: `KitchenTicket.Accepted/Started/Ready/Completed/Recalled/Cancelled`,
  `Order.ItemCancelled`, `Order.Served`.

## 9. Desktop screens

| Screen | Behaviour |
|---|---|
| Kitchen Display | Full-screen; three columns **NEW** (New + Accepted, with ACCEPT/START), **PREPARING** (READY button), **READY** (DONE + RECALL). Card: table code (large), ticket number, waiter, batch badge ("+ADD" for batch > 1), timer with colour escalation, items `qty x name`, notes in italic, modifiers indented, cancelled items struck through. New ticket: sound + highlight for 5 s. Header: station name, counts per column, connection indicator, **Sold out** button opens item availability list. |
| Completed Orders | Today's completed/cancelled tickets, timings (accept, prep, ready durations) |
| Waiter Table Details | Tickets listed with status chips; **SERVED** enabled when any ticket Ready; per-item **Cancel** respecting rules |
| Waiter notifications | Toast + banner "TABLE 05 — ORDER READY (Ticket 1001-1)"; My Orders highlights Ready rows |

## 10. Tests

- Domain: ticket transition table; order status derivation cases (mixed ticket states incl. appended
  batch after Served -> Submitted; all completed -> Served); item cancel rules by status/role.
- Application: submit with items for two stations creates two tickets with correct items; append
  creates a new ticket with batch 2; accept/start/ready/complete updates timestamps; recall; order
  cancel cancels open tickets; serve completes Ready tickets only; cancelling last item cancels ticket.
- Api: kitchen user without station sees all tickets; with `JoinStation` receives only its station's
  `KitchenTicketCreated`; waiter receives `OrderReady` on `user:{id}`; cashier cannot call
  `/api/kitchen/orders/{id}/ready` (403); table status Ready after ticket ready.
- Desktop: `KitchenDisplayViewModel` moves cards between columns on events, idempotent on duplicate
  events, full replace on `RefreshAsync`; timer colour thresholds.

## 11. Manual demo script

1. Configure KITCHEN-01 for MAIN and KITCHEN-02 for BAR. Waiter sends an order with biryani + coke:
   MAIN shows biryani ticket, BAR shows coke ticket, each within 1 s.
2. BAR presses START then READY: waiter gets "ORDER READY" toast; table tile turns Ready; order status
   shows Preparing (MAIN still working). Waiter presses SERVED: BAR ticket completed.
3. MAIN accepts, starts, marks ready; waiter serves; order Served; table Occupied.
4. Waiter appends dessert: new ticket on MAIN with "+ADD" badge; order back to Submitted/Preparing.
5. Unplug KITCHEN-01's network, send two orders, plug back in: both tickets appear after resync, no
   duplicates, timers correct.
6. Manager cancels an item while Preparing: ticket shows strike-through; waiter sees update.

## 12. Acceptance criteria

- [x] Tickets routed by station and visible within 1 s; full workflow with large buttons.
- [x] Order/table status derivation correct for multi-station and multi-batch orders.
- [x] Waiter notified on Ready and can serve; item cancellation rules enforced.
- [x] Kitchen display resyncs after disconnect with no duplicates (automated; the two-PC unplug demo is still to do).
- [x] Definition of Done satisfied.

## 13. Risks and notes

- Avoid chatty events: a single `ready` press emits at most `KitchenTicketUpdated`, `OrderReady`,
  `TableStatusChanged`.
- Keep ticket card rendering virtualised; a busy kitchen can have 40+ open tickets.
- Sound playback must not throw on machines without audio devices.

## 14. Changes during implementation

- **Derivation in one place.** `KitchenSync.Recompute` derives the order status from its tickets
  (`OrderStatusDeriver`, docs/02 § 4.1) and the table follows: the table is **Ready while any ticket is Ready**,
  even when the derived order status is still Submitted/Preparing because another station is working. An order
  whose tickets are all cancelled keeps its status. Order-level events fire only when the derived status changes;
  `OrderReady` fires whenever a ticket becomes Ready.
- **Ticket numbers** are `{order}-{batch}`, plus `-{station code}` when a batch spans stations (`1003-1-BAR`).
  On create-and-send the tickets are written in the same transaction, after the order number is assigned.
- **Station screens.** The station is chosen on the Kitchen Display itself (chips at the top) and saved in the
  terminal settings rather than on a separate settings page. The screen calls `JoinStation`, which also removes it
  from `role:kitchen`, so it receives only its station's events; "All stations" leaves the station.
- **Events only trigger a re-read.** The display fetches the ticket on every event, so duplicates and
  out-of-order events cannot corrupt the board; it also reloads everything every minute and after a reconnect.
- **Serve** may be pressed by any waiter (runners serve other waiters' tables); item cancel follows the order's
  ownership rules plus the ticket rules (New/Accepted: owner or manager; Preparing: manager; Ready/Completed: never).
- **Completed Orders** covers the current business day, using the `BusinessDayStartTime` setting.
- `OrderItems.KitchenOrderId` was not added: the item ↔ ticket link is `KitchenOrderItems.OrderItemId`.
- Kitchen **sold-out** list uses the existing availability endpoint and the cached menu.
- The ticket columns use a virtualising panel, so 40+ open tickets stay smooth.
