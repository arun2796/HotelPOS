# Phase 22 — Features excluded from Phase 21 (plan)

Status: In progress on `phase-22-excluded-features` — Foundations 0.1–0.2, Ordering O1–O4, Billing B1 and Finance F1–F4 built (2026-10-18); Operations P1–P6, the B2 spec and Verification 22.5 pending · Depends on: Phase 21

Phase 21 restyled every client without touching the server. Its § 3 "Out" list names the mock-up elements that had no
data behind them. This phase adds that data: entities, migrations, commands, endpoints and DTOs, then the UI on both
clients. Two read-only code audits (ordering/billing, finance/operations) back the facts below; paths are under
`src/`. The owner asked for the plan first; implementation starts on approval of § 4.

## 1. Goal

Give the server the data the mock-ups assume — order types, held orders, reservations, wallet, item-level bill split,
hourly sales, expenses with profit & loss, floor-plan geometry, staff on duty, kitchen ticket pictures, KOT printing
from the browser — without breaking the one-bill-per-order billing rules, the GST numbering, the table state machine
or the Phase 21 presentation contract (`StatusPresentation` grows; nothing else in Contracts/Presentation changes).

## 2. What the audits found

- **Nothing models a sales channel.** `Order.TableId` is non-nullable (`Domain/Orders/Order.cs:25`), `CreateOrder`
  validates `TableId > 0`, and `Order.Table!` is dereferenced across billing (`BillSteps`, `RequestBill`,
  `BillWorkflow.BillScope`, `ReopenBill`, `VoidBill`), `CancelOrder`, `KitchenTickets`, `DayEndChecks`, `BillEvents`
  and every `Table.FollowOrder/FollowKitchen/DetachOrder` call. Order types are the widest change in this phase.
- **Draft is already a hold** — saved server-side, items replaceable, cancellable — except that it pins the table
  through `AttachOrder`. A table-freeing hold needs a tableless draft, i.e. order types first.
- **No reservation entity**; `TableStatus` has no Reserved value and is mapped in `Table.FollowOrder`, the desktop
  map colours and the serialized events. Reservations must be time-derived, not a new status.
- **Wallet is already possible**: `PaymentMethod` is configurable (`Code`, `RequiresReference`, `SortOrder`), and
  Phase 21 renders one button per active method. A third-party wallet is a seed row. A stored-value customer wallet
  is customers/loyalty territory, which the owner excluded.
- **Item-level split is the riskiest feature**: one live bill per order is enforced by the filtered unique index
  `IX_Bills_OrderId_Live`; `BillWorkflow`, `Order.MarkPaid`, `table.Release`, `VoidBill` and `ReopenBill` assume it;
  invoice numbers are assigned at `Finalize`.
- **Hourly sales needs no schema change**: `Bill.SettledAt` and `Payment.PaidAt` are UTC timestamps; today's finest
  bucket is the business day (`ReportQueries.DailySalesAsync`, shifted by `ShiftMinutes`).
- **No expense record exists.** `CashMovement` (pay in/out) is shift-bound, has no category or payee, and mixes float
  adjustments with spend — not a substitute. P&L inputs (subtotal, discounts, tax, refunds, net) already exist in
  `DailySalesRowDto`; exports support XLSX/PDF (`ReportTables`, `ExportReport`) and CSV (`ReportCsv`).
- **Tables have no geometry** (`Domain/Floor/Table.cs`: code, name, section, capacity, status); the desktop map is a
  `WrapPanel` per section, the web a card list.
- **Presence data exists but is in-memory**: the hub connection tracker (user, roles, device, connected-at),
  `Device.LastSeenAt`, `User.LastLoginAt`, open `CashShift` rows, waiters with active orders. There is no attendance
  entity; a rota/clock-in is a separate module.
- **Ticket pictures are one DTO field away**: `KitchenTicketItemDto` lacks `ImageUrl`; the mapping in
  `KitchenTickets.cs:117` does not load `MenuItem`.
- **KOT printing is desktop-only**: `GET api/print/kot/{ticketId}` returns a JSON document, not a PDF; rendering and
  ESC/POS live in `Desktop/Services/Printing`; there is no server-side printer path. Invoice/receipt PDFs exist via
  `GetDocumentPdfCommand` + QuestPDF, so a KOT PDF is a layout, not new plumbing. Android MAUI BlazorWebView has no
  `window.print`.
- **The web has no admin pages** (only waiter/kitchen). Every admin-facing UI below is WPF (Phase 21 D1).

## 3. Scope

**In (11 features):** order types; held orders (cheap variant: list + resume, table stays pinned; table-freeing
variant after order types); reservations (time-derived); wallet as a configured payment method; item-level bill
split; hourly sales; expenses + profit & loss; floor-plan geometry with an admin editor and read-only maps; staff on
duty widget; kitchen ticket pictures; KOT PDF printed from the browser (Android via a native print bridge).

**Out:** stored-value customer wallets, loyalty, rota/attendance/clock-in, COGS/purchases in the P&L, recipes,
inventory, delivery dispatch/riders, online ordering channels, web admin pages.

## 4. Decisions needed from the owner

| # | Decision | Recommendation |
|---|---|---|
| E1 | **Order types.** Dine-in / Takeaway / Delivery as `OrderType` on `Order`, `TableId` nullable; delivery captures name, phone, address on the order | Yes. Default DineIn for existing rows; tableless orders get a synthetic label ("TA-1016" / "DL-1016") wherever `TableCode` is shown or printed; cashier queue groups by that label; reports gain an order-type breakdown. Kitchen: same stations, label on the ticket; a "Counter" hand-off chip replaces the table on takeaway tickets |
| E2 | **Held orders.** A) cheap: "Held" = saved Draft; list + Resume UI, table stays Ordering; B) table-freeing: `HeldAt/HeldBy/HoldReason` on Order, Hold detaches the table, Resume re-attaches or fails if taken | A first (0.5 backend); B only after E1 lands and only if the venue wants held drafts to free tables. Never a new `OrderStatus` value |
| E3 | **Reservations.** New `Reservation` entity (table, starts/ends, name, phone, party size, status Booked/Seated/Cancelled/NoShow, notes); "reserved soon" derived in `TableMapping`; no `TableStatus.Reserved` | Yes. Seat = blocked on Occupied tables; overlap with a running order is a warning (no duration estimate exists). Shown as a badge on waiter and admin tables, a count on the dashboard, a list + form under Live Operations |
| E4 | **Wallet.** A) configured method "Wallet" (code WALLET, reference required) seeded + `PaymentMethodCodes.Wallet`; B) stored-value customer wallet | **A.** B is customers/loyalty (excluded) and ≥ 4 days |
| E5 | **Item-level split.** `SplitBill(billId, billItemIds[], quantities?)` while Open/unclaimed/unpaid → sibling bill for the same order; lift `IX_Bills_OrderId_Live` to a `(OrderId, SplitIndex)` key; order completes when all live sibling bills settle; discount re-allocated by `DiscountShare`; each sibling gets its own invoice number at Finalize | Do it **last**, as its own step with a written rounding/discount spec, and only if the venue actually splits by item (amount split already exists). Splitting after Finalize is forbidden |
| E6 | **Hourly sales.** `HourlySalesAsync` (`date_trunc('hour', SettledAt + shift)`), zero-filled 24 business hours, on `DashboardDto.HourlyToday` + a Reports tab | Yes; desktop bars only (no web dashboard) |
| E7 | **Expenses + P&L.** `Expense` (date, category, amount, payee, note, paid-from: drawer pay-out / bank / other, created-by, soft-void) + `ExpenseCategory`; optional link from a drawer `CashMovement` pay-out to an Expense so cash spend is not double counted; permissions `ExpensesView/Manage`; P&L = net sales − discounts − refunds − expenses by category, period filter, XLSX/PDF/CSV | Yes, without attachments in this phase. P&L is labelled "Sales minus expenses" (no COGS). Expenses in a closed period are read-only |
| E8 | **Floor plan.** `Table` gains `PosX/PosY/Width/Height/Shape/Rotation` (nullable), `SaveFloorLayout(sectionId, …)` bulk command; WPF admin editor (drag, resize, snap, Admin-only); read-only canvas on cashier/admin and waiter web with fallback to the current list when a section has no coordinates | Yes, editor desktop-only; the phone keeps a list/map toggle because tiles are too small on 360 px |
| E9 | **Staff on duty.** `GetStaffOnDuty` = hub-connected users per device + open cash shifts + waiters with active orders → dashboard "Staff today" card (replaces today's placeholder) | Widget only; no rota/attendance. Visible to Manager/Admin (`ReportsView`) |
| E10 | **Ticket pictures.** `ImageUrl?` on `KitchenTicketItemDto` via a join on `MenuItemId` (no snapshot column); thumbnails on web `TicketCard`/`TicketDetailPane` and desktop KDS; station setting to hide them | Yes; images off by default on the KDS, on in the detail pane |
| E11 | **Print KOT from the web.** `GET api/print/kot/{id}.pdf` (QuestPDF, thermal `paper` param like receipts) + web Print button that reserves a copy (`POST kot/{id}/copies`), opens the PDF, completes the log; Android: native `PrintManager` bridge in the MAUI host | Browser first; Android bridge as a separate step the owner can drop. The hub-dispatch alternative (print on a desktop terminal) is out unless asked |

## 5. Deliverables by step (sizes in agent-days: backend / desktop / web / tests)

### 22.0 Foundations

| # | Step | Size |
|---|---|---|
| 0.1 | Migration `Phase22_Channels`: `Orders.OrderType` (default DineIn), `Orders.TableId` nullable, delivery fields; `OrderType` enum; `StatusPresentation.For(OrderType)` | 1 / – / – / 0.5 |
| 0.2 | Tableless order plumbing: `Order` ctor overload, `CreateOrder` validation by type, synthetic label helper used by `Bill.TableCode`, KOT, print, reports, events (`TableId?` in payloads), table attach/release skipped when null | 1.5 / – / – / 0.5 |

### 22.1 Ordering (E1–E3)

| # | Step | Size |
|---|---|---|
| O1 | Order type in the builders: desktop `OrderBuilderViewModel` and web `OrderBuilderPage` type chips; delivery details sheet; `TablePickerSheet` offers "Takeaway" / "Delivery" without a table; filter chips on the cashier workspace, Orders page, My orders; kitchen ticket label | – / 1.5 / 1.5 / 1 |
| O2 | Held orders A: `GET api/orders?status=Draft&mine` list, "Held" tab in My orders and Orders page, Resume → builder | 0.5 / 0.5 / 0.5 / 0.5 |
| O3 | Reservations backend: entity, config, overlap query, Create/Cancel/Seat/NoShow, `ReservationsController`, DTOs, permissions `ReservationsView/Manage`, realtime `ReservationChanged`, `TableMapping` "reserved soon" | 2 / – / – / 1 |
| O4 | Reservations UI: admin list + form (Live Operations → Reservations), table badge + Seat action on the desktop map and waiter `TableCard`, dashboard count | – / 2 / 1 / 0.5 |
| O5 | Held orders B (optional, after O1): hold/resume commands, table detach/re-attach, expiry sweeper | 1.5 / 0.5 / 0.5 / 0.5 |

### 22.2 Billing (E4, E5)

| # | Step | Size |
|---|---|---|
| B1 | Wallet: `PaymentMethodCodes.Wallet`, seed row, release note (existing DBs: admin adds it in Billing setup) | 0.1 / – / – / 0.2 |
| B2 | Item split spec (rounding, discount share, tax per sibling, void/reopen with siblings) — written and approved before code | 0.5 (doc) |
| B3 | Item split backend: `SplitBill`, `SplitIndex` + index migration, completion = all siblings settled, `VoidBill`/`ReopenBill` sibling rules, invoice numbering unchanged | 3 / – / – / 2 |
| B4 | Item split UI: "Split items" picker in the cashier workspace, sibling bills in the queue ("#1016-A/B"), print suffix; web order details shows sibling bill status | – / 2 / 1 / 0.5 |

### 22.3 Finance (E6, E7)

| # | Step | Size |
|---|---|---|
| F1 | Hourly sales query + DTO + `DashboardDto.HourlyToday` + Reports "Hourly" tab; desktop bars | 0.5 / 0.5 / – / 0.5 |
| F2 | Expenses backend: `Expense`, `ExpenseCategory`, CRUD, permissions, audit, period-close lock, optional pay-out link | 1.5 / – / – / 0.5 |
| F3 | Expenses UI: Finance → Expenses (list, filters, form, void), categories in Settings | – / 1.5 / – / 0.5 |
| F4 | P&L: `ProfitLossAsync` composing daily sales + expenses by category; Reports → Profit & Loss tab; XLSX/PDF/CSV via `ReportTables`/`ReportCsv`; dashboard KPI "Expenses today" | 1 / 0.5 / – / 0.5 |

### 22.4 Operations (E8–E11)

| # | Step | Size |
|---|---|---|
| P1 | Floor geometry backend: fields, migration with auto-grid defaults, `SaveFloorLayout`, DTOs, `FloorLayoutChanged` event | 1 / – / – / 0.5 |
| P2 | Desktop floor editor (Admin): canvas, drag/resize thumbs, snap, shapes, undo, save; read-only canvas for cashier/admin `TableMapView` with list fallback | – / 3 / – / 0.5 |
| P3 | Web floor map: scaled absolute layout on `TablesPage` with a list/map toggle | – / – / 1.5 / 0.5 |
| P4 | Staff on duty: `IPresenceTracker` abstraction over the hub tracker, `GetStaffOnDuty`, DTO, dashboard "Staff today" card | 1 / 1 / – / 0.75 |
| P5 | Ticket pictures: `ImageUrl?` on `KitchenTicketItemDto` (join, no N+1), web `TicketCard`/`TicketDetailPane`, desktop KDS, hide-images station setting | 0.5 / 0.5 / 0.5 / 0.5 |
| P6 | KOT PDF: `kot/{id}.pdf` via `GetDocumentPdfCommand` + thermal layout; web Print button with copy reservation + completion | 1 / – / 1 / 0.75 |
| P7 | Android print bridge (optional): MAUI `IPrintService` → `PrintManager` with the PDF | – / – / 1.5 / 0.25 |

### 22.5 Verification

Full suite; migrations applied to a copy of a venue database (`dev-setup.md` database commands); galleries for the
changed screens only (web-flow steps for takeaway, reservation badge, ticket pictures, Print; desktop live passes for
the workspace split, expenses, P&L, floor editor); release notes listing the new permissions, the Wallet method and
the migrations; `docs/02`, `03`, `04`, `07` updated.

## 6. Order and effort

0.1 → 0.2 first (everything tableless depends on it). Then four tracks in parallel: Ordering (O1–O4), Finance
(F1–F4), Operations (P1–P6), Billing (B1, then the B2 spec). B3–B4 start only after the B2 spec is approved and the
other tracks are merged. O5 and P7 are optional tail steps.

| Track | Agent-days |
|---|---|
| Foundations | 3.5 |
| Ordering | 11–13 (O5 adds 3) |
| Billing | 0.3 + 0.5 spec; item split (B3–B4) 8.5 |
| Finance | 7.5 |
| Operations | 13.5 (P7 adds 1.75) |
| Verification | 3 |
| **Total** | **39–44 without item split and optional steps; 52–57 with everything** |

## 7. Data model, API, events

Migrations: `Phase22_Channels` (Orders), `Phase22_Reservations`, `Phase22_Expenses`, `Phase22_FloorLayout` (Tables),
`Phase22_BillSplit` (Bills index + `SplitIndex`) — each its own migration so tracks can land independently.
New endpoints: reservations CRUD, `orders?status=Draft`, `bills/{id}/split`, reports `hourly` and `profit-loss`,
expenses CRUD + categories, `floor/sections/{id}/layout`, `staff/on-duty`, `print/kot/{id}.pdf`. Additive DTO changes:
`OrderDto.OrderType/Delivery*`, `TableDto.Pos*/Reservation*`, `KitchenTicketItemDto.ImageUrl`,
`DashboardDto.HourlyToday/StaffOnDuty/ExpensesToday`, `BillDto.SplitIndex/SiblingBillIds`. New realtime events:
`ReservationChanged`, `FloorLayoutChanged`, `ExpenseRecorded` (dashboard refresh). New permissions:
`ReservationsView/Manage`, `ExpensesView/Manage`, `FloorLayoutManage`; default-role seed in each migration.

## 8. Tests

Domain: tableless order lifecycle, hold/resume, reservation overlap, split rounding (property-style over random
lines), expense void/lock. Application: each command/query, permission matrix (the repo-scanning `AuthorizationMatrix`
test must know every new route), report buckets across the business-day shift. API: contract tests for the additive
DTOs. Desktop/web: VM and bUnit tests per surface; rendering counts; `StatusPresentationTests` for `OrderType`.

## 9. Risks

- Tableless orders touch every `Order.Table!` site; one missed null is a runtime crash in billing. Mitigate with the
  synthetic-label helper and a Domain test that walks the whole lifecycle without a table.
- Item split can produce paise differences and double-counted per-table reports; the B2 spec and a reconciliation
  test (sum of siblings == original) are mandatory.
- Hub presence is lost on API restart and flaps on phones; the Staff card must say "live, not attendance".
- The floor editor is the largest UI effort and the hardest to test; keep the list fallback so nothing depends on it.
- Expenses next to drawer pay-outs invite double counting; the P&L must show which pay-outs are linked.
- Timezone / business-day shift off-by-one in hourly buckets — reuse `ReportRanges` exactly.
- Migrations on a live venue database: run on a copy first (22.5).

## 10. Acceptance criteria

- [ ] Owner has signed off E1–E11 before the related step starts.
- [ ] A takeaway order can be created, cooked, billed and paid with no table; a dine-in order behaves exactly as today.
- [ ] A reservation shows on the waiter and admin tables, can be seated, and never changes `TableStatus` directly.
- [ ] Wallet appears as a payment button once configured; reports group it like any method.
- [ ] Hourly chart and P&L match the daily totals for the same range (reconciliation test).
- [ ] Expenses respect permissions and period close; P&L exports in all three formats.
- [ ] Floor plan edits on one terminal appear on the others; the phone keeps a usable list.
- [ ] Kitchen tickets show pictures when enabled; the kitchen query count does not grow with items (no N+1).
- [ ] A waiter on a desktop browser prints a KOT PDF and the print log records the copy.
- [ ] (If B3–B4 are built) siblings sum to the original bill; GST register totals unchanged.
- [ ] Full suite green; migrations applied to a venue-copy database; docs and release notes updated.

## 11. Changes during implementation

- **P1 (2026-10-18).** Geometry is `int` layout units (px at 100%) with `TableShape` (Rectangle=1, Round=2) and
  `Rotation` 0-359, guarded by `CK_Tables_Layout` (all six null or all valid). Bounds: position 0-5000, size 40-600,
  at most 500 items per save. The migration fills a default grid for active tables only: per section ordered by
  `Code` (tables have no `SortOrder`), 6 per row, 120x80 rectangles, 24-unit gap and margin
  (`x = 24 + col*144`, `y = 24 + row*104`); inactive and later-created tables stay unplaced. The migration also
  inserts `floor.layout.manage` for the Admin role (role permissions live in the `RolePermissions` table; Admin is
  `Permissions.All` in code, so no Manager grant). Route is `api/floor/sections/{id}/layout` (new
  `FloorLayoutController`), GET uses `tables.view`, so waiters can read it. `PUT` saves only the listed tables and
  returns the refreshed layout; the event payload is `FloorLayoutChangedEvent { SectionId }`. Audit action
  `Floor.LayoutSaved` on the Section entity.

- **F4 (2026-10-18).** No `ProfitLossAsync` SQL: the handler composes `DailySalesAsync` (sales side) with a new
  `ExpensesByCategoryAsync` (live expenses per category, `ExpenseDate` compared as calendar dates, inclusive) in
  `ProfitLossStatement.Compose`, which is pure and unit-tested. The statement is rows of Section / Line / Amount with
  magnitudes and "Less: …" / "Add: …" labels (no negative numbers); `Amount` is `[NoTotal]` so no export or grid sums
  the lines. Round off is derived (`Gross − (Subtotal − Discounts + Tax)`) and shown only when non-zero. The dashboard
  gains `ExpensesToday` and a fifth KPI card (strip widened to five columns) that deep-links to the P&L. Permission is
  `ReportsView` (it is a report), not `ExpensesView`.
