# Phase 3 — Menu & Pricing

Status: Done (2026-10-07) · Depends on: Phase 1

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

- [x] All menu master data manageable from the desktop with validation and audit.
- [x] Compact menu endpoint with version check; `MenuChanged` propagates to clients.
- [x] Image upload and display working; images cached on clients.
- [x] Definition of Done satisfied. (Demo steps 2–4 were run against the real API and in automated tests; the
  two-terminal run on the target hardware is still to do, as for Phase 2.)

## 13. Risks and notes

- Keep `MenuDto` small (no descriptions longer than needed, no base64 images) so a 500-item menu loads
  in under 300 ms on the LAN.
- Tax-inclusive pricing is out of scope; keep `PricesIncludeTax` setting reserved and unused.

## 14. Changes during implementation

- **Atomic menu version.** Every menu write goes through `MenuChanges`: the change, its audit rows and the
  `MenuVersion` bump (one `UPDATE ... RETURNING` on the settings row) share one transaction, then one
  `MenuChanged` is published. Concurrent writes serialise on that row and never lose a bump (tested with six
  parallel writes). `MenuVersion` can no longer be edited through the settings screen.
- **DTOs.** `MenuItemDto` carries the detail fields (description, modifier group ids), so there is no separate
  `MenuItemDetailDto`. Stations, taxes and modifier groups/options use one `Save...Request` for create and
  update (`isActive` is ignored on create). The ordering menu uses compact `MenuEntryDto` /
  `MenuModifierGroupDto` records.
- **Images.** Stored by `MenuImageStore` (SkiaSharp, MIT): JPEG/PNG only (checked by decoding, not by file
  name), EXIF orientation applied, max 512 px, JPEG on a white background. Each upload gets a new name
  (`{id}-{random}.jpg`) and the old file is deleted, so files are served with a one-year immutable cache header
  and terminals cache them by name. The folder is `Media:RootPath` (default next to the API) and must be backed
  up with the database (Phase 10 updated). Pictures can be attached once the item is saved.
- **Sold-out toggle** uses the item's row version like any write; a simultaneous edit returns
  `CONCURRENCY_CONFLICT` with the current item rather than overwriting it.
- **Inactive modifier groups** stay linked to items but are left out of the ordering menu; modifier option names
  are unique within their group.
- **Desktop.** One "Menu" page with tabs Items, Categories, Modifiers, Taxes, Stations (the last two for Admin
  only, matching the API) and Preview, which shows the menu exactly as terminals receive it from `MenuCache`
  and updates live on `MenuChanged`. `MenuCache` starts at sign-in for every role, reloads on a newer version
  or after a reconnect, keeps the old menu if a reload fails, and caches pictures in
  `%LocalAppData%\HotelPOS\cache\images`.
- **Kitchen sold-out button** is in the API now (`PATCH availability`, Kitchen allowed); the kitchen screen
  that uses it comes with Phase 5.
