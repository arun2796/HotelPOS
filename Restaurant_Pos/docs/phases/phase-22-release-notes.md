# Phase 22 — Release notes (draft for the venue upgrade)

Branch `phase-22-excluded-features`, 2026-10-19. Read together with `docs/ops/04-upgrade.md`.

## What the venue gets

- **Order types.** Dine-in, Takeaway and Delivery on the order builder (desktop and phone). Takeaway/Delivery orders
  have no table and show as `TA-1016` / `DL-1016` on the cashier queue, kitchen tickets, receipts and reports.
  Delivery captures name, phone and address. Existing orders are Dine-in.
- **Held orders.** Saved drafts appear under a "Held" tab (My orders, Orders) and can be resumed. The table stays
  in Ordering while a draft is held.
- **Reservations.** Live Operations → Reservations (list + form); table badge "reserved soon" on the desktop map and
  the waiter's table cards; Seat / Cancel / No-show; dashboard count. Table status itself never changes to "Reserved".
- **Wallet payment method.** `WALLET` (reference required) is seeded on new databases. **Existing databases:** add it
  once in Billing setup → Payment methods (code `WALLET`, reference required); reports group it like any method.
- **Hourly sales.** Reports → Hourly sales (24 business hours, zero-filled) and a bar chart on the dashboard.
- **Expenses and Profit & Loss.** Finance → Expenses (record, filter, void; categories in Settings; optional link to a
  drawer pay-out so cash is not counted twice; read-only once the period is closed). Reports → Profit & Loss
  ("sales minus expenses", no cost of goods) with CSV/XLSX/PDF. Dashboard card "Expenses today".
- **Floor plan.** Admin → Floor plan editor (drag, resize, snap, rotate, round/rectangle, undo, save). Cashier/Admin
  table map and the waiter's phone get a List / Map toggle; the map shows only sections that have a plan.
- **Staff today.** Dashboard panel of who is connected, on a cash shift or serving orders. It shows **live
  connections, not attendance**: a phone that drops Wi-Fi disappears until it reconnects.
- **Kitchen ticket pictures.** Item thumbnails on the web ticket detail pane (on by default) and, when the station
  setting is turned on, on ticket cards and the desktop KDS.
- **Print KOT from the browser.** A Print KOT button on the waiter's order details and the kitchen ticket pane opens a
  thermal-width PDF; the print log records the copy like a terminal print. (Android native printing is not included.)

## Database migrations (applied automatically at API start when `Database:AutoMigrate` is on)

| Migration | Effect |
|---|---|
| `Phase22_Channels` | `Orders.OrderType` (default Dine-in), `Orders.TableId` nullable, delivery columns, index on `OrderType` |
| `Phase22_Reservations` | new `Reservations` table and indexes; permissions `reservations.view` / `reservations.manage` |
| `Phase22_Expenses` | new `ExpenseCategories` (six seeded) and `Expenses` tables |
| `Phase22_ExpensePermissions` | grants `expenses.view` / `expenses.manage` to the existing Admin and Manager roles (idempotent) |
| `Phase22_FloorLayout` | `Tables.PosX/PosY/Width/Height/Shape/Rotation` with a default 6-per-row grid for existing active tables; permission `floor.layout.manage` for Admin |

Take a backup before upgrading (`docs/ops/03-backup-and-recovery.md`). All four migrations are additive; none drops or
rewrites data. Rehearse on a copy of the venue database first (`docs/dev-setup.md`, database commands).

## New permissions and who has them by default

| Permission | Admin | Manager | Waiter | Kitchen | Cashier | Used by |
|---|---|---|---|---|---|---|
| `reservations.view` | ✓ | ✓ | ✓ | – | ✓ | reservation list, table badges |
| `reservations.manage` | ✓ | ✓ | ✓ | – | ✓ | create / seat / cancel / no-show (waiters and cashiers take bookings at the counter) |
| `expenses.view` | ✓ | ✓ | – | – | – | Expenses screen, P&L is under `reports.view` |
| `expenses.manage` | ✓ | ✓ | – | – | – | record, edit, void expenses and categories |
| `floor.layout.manage` | ✓ | – | – | – | – | Floor plan editor and `PUT /api/sections/{id}/layout` |

Custom roles created by the venue do not receive the new permissions automatically: grant them in Roles & permissions.

## New API routes (for integrators)

`GET /api/reports/sales/hourly`, `GET /api/reports/profit-loss`, `GET /api/expenses…` (CRUD + categories),
`GET/POST /api/reservations…`, `GET /api/orders?status=Draft`, `GET/PUT /api/sections/{id}/layout`,
`GET /api/staff/on-duty`, `GET /api/print/kot/{ticketId}.pdf?paper=80&logId=`. Additive DTO fields:
`OrderDto.OrderType/Customer*/Delivery*`, `TableDto.Pos*/Shape/Rotation`, `KitchenTicketItemDto.ImageUrl`,
`DashboardDto.HourlyToday/ExpensesToday/StaffOnDuty`. New realtime events: `ReservationChanged`, `ExpenseChanged`,
`FloorLayoutChanged`. Details in `docs/03-api-reference.md` and `docs/04-realtime-events.md`.

## Not in this release

Item-level bill split (spec `phase-22-item-split-spec.md`, awaiting the owner's decision), table-freeing held orders
(O5), Android native KOT printing (P7), stored-value customer wallets, loyalty, attendance, COGS/inventory.

## Known limitations

- Staff today is lost on API restart until clients reconnect (by design; it is presence, not attendance).
- The floor plan editor runs on the desktop only; phones show the plan read-only with a 44 px minimum tap target
  and scroll sideways below 50 % scale.
- A browser pop-up blocker stops the KOT PDF from opening; the print log then records the copy as failed.
