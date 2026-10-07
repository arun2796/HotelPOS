# Phase 2 — Tables & Sections

Status: Done (2026-10-07) · Depends on: Phase 1

## 1. Goal

Admins and managers define the floor (sections and tables). Waiters see a live table map, can mark a
table occupied with a guest count and release it. Two waiters cannot occupy the same table at the
same time. This phase also delivers the first real-time event (`TableStatusChanged`) and the resync
pattern that every later screen reuses.

## 2. Prerequisites

- Phase 1 (auth, shell, hub, API skeleton).
- `TableStatus` enum and derivation rules in `docs/02` § 4.3.

## 3. Scope

**In**
- `Section`, `Table` entities; CRUD; activate/deactivate.
- Table map query (sections with tables, status, guest count, occupied time; current order summary
  fields exist but stay null until Phase 4).
- Occupy / release / out-of-service with `RowVersion` concurrency.
- `TableStatusChanged` event and client resync on reconnect.
- Waiter Table Map and Table Details (status part only); Admin Sections/Tables screens.

**Out**
- Orders on tables (Phase 4), reservations, table merging/transfer (deferred; see § 13).

## 4. Deliverables by project

| Project | Deliverables |
|---|---|
| Contracts | `SectionDto`, `CreateSectionRequest`, `UpdateSectionRequest`, `TableDto`, `TableMapDto` (sections -> tables), `TableDetailDto`, `CreateTableRequest`, `UpdateTableRequest`, `OccupyTableRequest`, `TableStatusChangedEvent` |
| Domain | `Section`, `Table` (+ `Occupy(guestCount)`, `Release()`, `SetOutOfService()`, status guards) |
| Application | `ISectionService`, `ITableService` (`GetMapAsync`, `GetAsync`, CRUD, `OccupyAsync`, `ReleaseAsync`, `SetServiceStateAsync`), validators |
| Infrastructure | configurations, `Phase02_Tables` migration, dev seed (Ground Floor T01–T08, First Floor T11–T14, Outdoor O01–O04) |
| Api | `SectionsController`, `TablesController`; publish `TableStatusChanged` |
| Desktop | Waiter: `TableMapView/VM`, `TableDetailsView/VM` (occupy with NumPad, release); Admin: `SectionsView/VM`, `TablesView/VM`; `TableTile` control with status colours; resync on `Reconnected` |
| Tests | see § 10 |

## 5. Data model

`Sections`, `Tables` as in `docs/02` § 2.2. Migration `Phase02_Tables`.
Unique `Tables.Code`; `Sections.Name` unique; `Tables.RowVersion`.

## 6. API endpoints

`docs/03-api-reference.md` § 3.3.

## 7. Real-time events

`TableStatusChanged { tableId, tableCode, status, orderId?, orderNumber? }` to all roles on
occupy, release, out-of-service/in-service (and in later phases on every order/bill transition).

## 8. Business rules

- Table code unique, 1–10 chars, uppercase on save. Capacity 1–50.
- Occupy: only from `Available`; request includes `rowVersion`; mismatch or wrong state -> 409
  (`CONCURRENCY_CONFLICT` / `TABLE_NOT_AVAILABLE`) with current table in `data`.
- Release: from `Occupied` with no active order by any waiter/cashier; if an active order exists
  (Phase 4+) only a Manager can release, and only after the order is cancelled/closed.
- Out-of-service only when `Available`; in-service returns to `Available`.
- Deactivating a table requires `Available`/`OutOfService`; deactivating a section requires all its
  tables inactive.
- `GET /api/tables` returns only active tables unless `includeInactive=true` (admin).
- Audit: `Table.Created/Updated/Deactivated`, `Section.*`, `Table.ReleasedByManager`.

## 9. Desktop screens

| Screen | Behaviour |
|---|---|
| Table Map (Waiter/Cashier/Manager) | Section tabs or grouped grid; tiles show code, status colour, guests, minutes since occupied; legend; refresh button; auto-updates on `TableStatusChanged`; resync on reconnect |
| Table Details panel | Code, section, capacity, status, occupied since, guests; buttons **OCCUPY** (NumPad for guests), **RELEASE**; order section shows "No order" (Phase 4 fills it) |
| Admin Sections | Grid + side panel (name, sort order, active) |
| Admin Tables | Grid (filter by section) + side panel (code, name, section, capacity, active), out-of-service toggle |

## 10. Tests

- Domain: `Table.Occupy` from each status (allowed only from Available); `Release` rules.
- Application: occupy with stale rowVersion -> `CONCURRENCY_CONFLICT`; two concurrent occupies -> one
  succeeds, the other 409; deactivate section with active tables rejected; duplicate code rejected.
- Api: `POST /api/tables` as Waiter -> 403; occupy flow 200 then 409; `TableStatusChanged` received by
  a test hub client; map excludes inactive tables.
- Desktop: `TableMapViewModel` applies `TableStatusChanged` idempotently; `RefreshAsync` replaces state.

## 11. Manual demo script

1. Admin creates sections and tables; waiter screen shows them without restart (`TableStatusChanged`
   is not needed for creation; the map refreshes on navigation and on a `MenuChanged`-style notice —
   use the refresh button or re-open the map).
2. Waiter A and Waiter B open Table T05 on two machines. A occupies with 4 guests; B's tile turns blue
   within 1 s. B presses Occupy -> message "Table was changed by another terminal" and refreshed state.
3. Disconnect Waiter B's Wi-Fi; A releases T05; reconnect B -> B's map shows T05 available after
   resync without user action.
4. Admin sets T08 out of service; waiters see grey tile.

## 12. Acceptance criteria

- [x] Admin CRUD for sections and tables with validation and audit.
- [x] Live table map on waiter terminals updated by events and by resync.
- [x] Concurrency control proven by the two-terminal test (automated: two concurrent occupies against
  PostgreSQL, a lost update rejected by `xmin`, and occupy 200 -> 409 through the real API; the manual
  two-PC run is still to be done on the target hardware).
- [x] Definition of Done satisfied.

## 13. Risks and notes

- Table transfer/merge is a common restaurant need; design `Order.TableId` to be updatable with an
  audited `transfer` endpoint in a later phase. Do not build now.
- Keep the tile virtualised; venues with 100+ tables must still render instantly.

## 14. Changes during implementation

- **PostgreSQL.** Built on PostgreSQL instead of SQL Server (see Phase 1 § changes). `Tables.RowVersion` is
  the `xmin` system column; the migration `Phase02_Tables` creates `Sections` and `Tables` with check
  constraints on status, capacity (1–50) and guest count.
- **Event payload.** `TableStatusChanged` also carries `guestCount` and `occupiedAtUtc` (and the row version in
  `entityVersion`), so a map updates a tile without a REST call and the next occupy sends a fresh row version.
- **Stale events.** `GET /api/tables` returns `serverTimeUtc` (taken before the read). A tile applies an event
  only if it is not older than the state it shows, and a refresh keeps a tile that already shows a newer
  event. This makes event handling idempotent and order-independent.
- **Roles.** Occupy/release also allow Admin (Admin holds every permission and sees the Tables module).
  `includeInactive` on `GET /api/tables` and `GET /api/sections` is allowed for Admin and Manager.
- **Deactivate/activate** are also possible through `PUT` (`isActive`), which the admin side panels use;
  `DELETE` remains the explicit deactivate endpoint. Both enforce the same rules.
- **Release with an order** is refused for now (`BUSINESS_RULE`); the Manager-only path and the
  `Table.ReleasedByManager` audit arrive with orders in Phase 4.
- **Audit** also records `Table.Activated/OutOfService/InService` and `Section.Activated`. Occupy and release
  are not audited (high volume; they will be visible through orders).
- **Desktop.** One "Sections & Tables" page with Tables/Sections tabs (`FloorViewModel` hosting
  `TablesViewModel` and `SectionsViewModel`); the table details panel is part of the map page. Tiles are in a
  `WrapPanel` per section with section filter chips. The panel is not virtualised (§ 13); it has not been
  measured with 100+ tables yet. If that is slow, switch to a virtualising wrap panel.
- **Demo seed** runs only on an empty floor, so it never re-adds tables an admin removed.
