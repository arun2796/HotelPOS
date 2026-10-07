# Phase 3 — Menu & Pricing

Status: Not started · Depends on: Phase 1 (can run in parallel with Phase 2)

## 1. Goal

Admins and managers maintain the menu: categories, items with prices and tax, modifier groups and
options, taxes, preparation stations, images and availability. Clients receive a compact, versioned
menu suitable for fast ordering, and are notified when it changes.

## 2. Prerequisites

- Phase 1. `PreparationStation` and `Tax` modelling in `docs/02` § 2.3.

## 3. Scope

**In**
- Entities and CRUD for categories, menu items, modifier groups/options, taxes, stations.
- Menu item image upload (API stores file, serves via static files).
- Availability toggle (sold out) usable by kitchen and managers.
- `GET /api/menu` compact endpoint with `MenuVersion` setting and `MenuChanged` event.
- Desktop admin screens; desktop `MenuCache` service (used by Phase 4 order builder).

**Out**
- Combo/set meals, time-based menus, price lists per section, stock levels (deferred).

## 4. Deliverables by project

| Project | Deliverables |
|---|---|
| Contracts | `CategoryDto`, `MenuItemDto`, `MenuItemDetailDto`, `ModifierGroupDto`, `ModifierOptionDto`, `TaxDto`, `StationDto`, create/update requests for each, `SetAvailabilityRequest`, `MenuDto` (version + categories + items + modifier groups), `MenuChangedEvent` |
| Domain | `Category`, `MenuItem`, `ModifierGroup`, `ModifierOption`, `MenuItemModifierGroup`, `Tax`, `PreparationStation` (exists from Phase 1) |
| Application | `ICategoryService`, `IMenuItemService` (CRUD, `SetAvailability`, `UploadImage`), `IModifierService`, `ITaxService`, `IStationService`, `IMenuQuery.GetMenuAsync(version?)`, `MenuVersionService.Bump()`; validators |
| Infrastructure | configurations, `Phase03_Menu` migration, image storage (`wwwroot/images/menu/{id}.jpg`, resized to max 512 px), dev seed menu (Biryani, Starters, Breads, Beverages…) |
| Api | `CategoriesController`, `MenuItemsController`, `ModifierGroupsController`, `TaxesController`, `StationsController`, `MenuController`; static files; publish `MenuChanged` |
| Desktop | Admin: Categories, Menu Items (grid + edit form + image picker + modifier group assignment), Modifier Groups/Options, Taxes, Stations; `IMenuCache` (loads `GET /api/menu`, refreshes on `MenuChanged`, caches images on disk) |
| Tests | see § 10 |

## 5. Data model

`docs/02` § 2.3. Migration `Phase03_Menu`. `Settings.MenuVersion` incremented on every change.

## 6. API endpoints

`docs/03-api-reference.md` § 3.4.

## 7. Real-time events

`MenuChanged { menuVersion }` to all roles after any menu write (debounced: one event per request).

## 8. Business rules

- Item name unique within category; price `>= 0`; tax optional; station required.
- Modifier group: `0 <= MinSelections <= MaxSelections`; an item can link many groups.
- Price change writes audit `MenuItem.PriceChanged` with old/new price.
- Deactivating a category requires all items inactive (or cascades with confirmation flag
  `deactivateItems=true`).
- Deactivating a tax or station used by active items is rejected.
- `GET /api/menu` includes only active categories, active items (with `isAvailable` flag so sold-out
  items can be shown greyed), active modifier groups/options; `?version=N` equal to current returns
  `{ notModified: true, version: N }`.
- Images: JPEG/PNG up to 2 MB; server resizes and stores; DTO carries a relative URL.
- Audit: `Category.*`, `MenuItem.Created/Updated/PriceChanged/AvailabilityChanged/Deactivated`,
  `Tax.*`, `Station.*`, `ModifierGroup.*`.

## 9. Desktop screens

| Screen | Behaviour |
|---|---|
| Admin Categories | Grid with sort order drag or up/down, add/edit panel, activate/deactivate |
| Admin Menu Items | Grid (search, category filter, active filter), edit form: name, code, category, price, tax, station, description, image, availability, active, modifier groups (checklist) |
| Admin Modifier Groups | Groups grid, options grid per group with price delta |
| Admin Taxes / Stations | Simple grids with add/edit |
| Kitchen / Manager | "Mark sold out" action available in Phase 5 kitchen screen (uses `PATCH availability`) |

## 10. Tests

- Application: duplicate item name in category rejected; price change creates audit; deactivating
  station in use rejected; `GetMenuAsync` excludes inactive and returns `notModified` for current
  version; menu version bumps on each write.
- Api: Cashier cannot `PUT /api/menu-items/{id}` (403); Kitchen can `PATCH availability`; image upload
  rejects 5 MB file; `MenuChanged` received by hub client after update.
- Desktop: `MenuCache` reloads on `MenuChanged` and keeps old data when the reload fails.

## 11. Manual demo script

1. Admin creates categories, stations (MAIN, BAR), taxes (GST 5%), items with images and modifiers.
2. On a waiter terminal, open the menu preview (Admin > Menu Items in read mode or the Phase 4 order
   builder later): items visible. Admin changes a price; waiter terminal receives `MenuChanged` and
   shows the new price within 2 s.
3. Kitchen user marks "Chicken 65" sold out; waiter sees it greyed.
4. Disconnect the waiter terminal, change two items, reconnect: menu refreshed after resync.

## 12. Acceptance criteria

- [ ] All menu master data manageable from the desktop with validation and audit.
- [ ] Compact menu endpoint with version check; `MenuChanged` propagates to clients.
- [ ] Image upload and display working; images cached on clients.
- [ ] Definition of Done satisfied.

## 13. Risks and notes

- Keep `MenuDto` small (no descriptions longer than needed, no base64 images) so a 500-item menu loads
  in under 300 ms on the LAN.
- Tax-inclusive pricing is out of scope; keep `PricesIncludeTax` setting reserved and unused.

## 14. Changes during implementation

(fill in while building)
