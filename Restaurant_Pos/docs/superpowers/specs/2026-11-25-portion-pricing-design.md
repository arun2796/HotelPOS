# Portion-based pricing, item images and menu redesign (design)

Date: 2026-11-25 · Size: project · Status: waiting for owner approval

## Intent and why
Today an item has one price. A restaurant sells the same dish in several portions (Quarter, Half, Full, 1 KG, 500 GM, 250 ML...),
each with its own price. The owner wants to create the item **once** with all its priced portions, let waiters and cashiers pick a
portion in one tap, and have the portion and its price carried, frozen, through the order, the kitchen ticket, the bill, the invoice
and the reports. Item images already exist; the menu cards and the admin screens are redesigned around portions.

## Goals
- An item has 1..12 portions: name, optional quantity + unit, price, default marker, on/off, sort order.
- Waiter (web/Android) and cashier/captain (Windows) pick a portion; the price updates at once; two portions of one item are two cart lines.
- The chosen portion and its unit price are **frozen on the order line**; later price edits never change an existing order or bill.
- KOT, kitchen display, order details, bill, invoice and reports show "Chicken Biryani (Half)". Tax, discount, packing charge and modifiers keep working as today.
- Admin: item form with a Portions table; item list shows the picture, portions and prices; photo can be chosen while creating.
- Existing items, orders, bills, invoices and API consumers keep working with no data loss.

## Non-goals
- Stock stays per item. **One portion sold takes 1 off the item's count** (owner's choice); the Exact / Approximate modes are unchanged. No per-portion stock, no fractions.
- No per-portion picture, no per-portion tax, kitchen station or packing charge (packing charge stays per item, added once per unit).
- No portions column in the Excel menu import (a price still imports as the item's one "Regular" portion). Later work.
- No change to modifier groups. Restaurants that fake sizes with a "Size +90" modifier group keep working; moving them is the owner's choice later.
- No new image formats: the existing upload accepts JPEG and PNG, up to 2 MB, re-encoded to a 512 px JPEG. (Earlier mock-up said WebP: not included.)

## Anti-goals
Anything that changes an old order, bill or report row; a price taken from the client; an item that becomes unsellable after the migration; a second source of truth for the price that can drift.

## Constraints and findings from the code
- `OrderItem.ItemName` is already the frozen name and **already carries a " (Parcel)" suffix**; the clients strip it back by string matching (`OrderComposer.LoadOrder`, `OrderBuilderViewModel`). The portion must not be glued into that text.
- `BillItem` keeps its own frozen copy of the name; `KitchenOrderItem` has none (it reads the order line).
- The server computes the price from the menu (`OrderItemFactory`); the client price is never trusted. Portions follow the same rule: the client sends only `PortionId`.
- Menu is cached by version (`MenuChanges.SaveAsync` bumps it); every portion write goes through that path.
- Stock is an integer per item (`OrderStock` groups by `MenuItemId`); it is not touched.
- The unit-test helpers call `new OrderItem(...)`, `new MenuItem(...)`, `new BillLineInput(...)`: new parameters must be optional.
- The word "portion" already appears in stock text ("portion quota"). Stock text is left alone; new screens say "Portion".

## Approaches considered
1. **Structured portion on the order line (chosen by the owner).** New frozen fields on the order line and bill line; a composed `DisplayName`; reports can group by item or portion.
2. Glue the portion into the item name text. Smallest change but the portion becomes unqueryable text and collides with the "(Parcel)" suffix handling. Rejected.
3. Each portion as its own menu item. Breaks "create once", splits stock and images. Rejected.
Pick of presentation (owner): **A, portion chips on the item card, then an Add button** (mock-up reviewed 2026-11-25).

## Design decisions

### Data
- New table `MenuItemPortions`: `Id, MenuItemId (FK, cascade), Name (<=40), Quantity (decimal, nullable, >0), Unit (nullable; fixed list: GM, KG, ML, L, PCS, PLATE), Price (>=0, <= MenuLimits.MaxPrice), IsDefault, IsActive, SortOrder`.
  Unique `{MenuItemId, Name}` (case-insensitive); partial unique index so only one default per item.
- Rules (domain, `MenuItem.SetPortions`): 1..12 portions; unique names; at least one active; **exactly one default and it must be active**.
- `MenuItem.Price` is kept and **always equals the default portion's price** (set by `SetPortions`). Old API clients, import, reports and the "price changed" audit keep reading it.
- Order line (`OrderItems`): add `PortionId` (nullable FK, **ON DELETE SET NULL**), `PortionName` (<=40), `PortionQuantity`, `PortionUnit`. Bill line (`BillItems`): add `PortionName`, `PortionQuantity`, `PortionUnit` (no FK).
- **When the portion is stored:** only when the item has more than one portion **defined** (active or not) at order time. A one-portion item keeps the old line exactly (no portion fields, name unchanged). Chips are shown when 2 or more are **active**; an item with Half and Full where Full is switched off shows no chips, adds on one tap, and its line still stores "Half". (My call.) Old lines and lines of one-portion items have no portion and show a blank Portion in reports (their own row); history is not rewritten.
- `DisplayName` (domain, on `OrderItem` and `BillItem`): the frozen name with the portion inserted before a trailing " (Parcel)": `Chicken Biryani (Half)`, `Chicken Biryani (Half) (Parcel)`. Lines without portion data show the old name.
- Migration `Phase32_ItemPortions`: create the table; backfill one "Regular" default portion per existing item with its current price; add the nullable columns; old order and bill rows stay null. Down drops them. Rehearse on a copy of the dev database as in `docs/ops/11-seat-groups-release.md` § 3.

### API (all additive)
- `MenuItemPortionDto { Id, Name, Quantity, Unit, Price, IsDefault, IsActive, SortOrder }` and `SaveMenuItemPortionRequest` (same, `Id` optional).
- `CreateMenuItemRequest` / `UpdateMenuItemRequest`: optional `Portions`. Create without it makes one "Regular" portion at `Price`. Update without it leaves portions untouched; if `Price` differs from the default's price the default portion's price is changed (old clients editing the price keep working). With it, the list replaces the item's portions (existing ids keep their identity, so frozen order lines keep their link); `Price` is ignored.
- `MenuItemDto` (admin) lists all portions; `MenuEntryDto` (ordering menu) lists **active portions only**, default first by `SortOrder`; `Price` = default portion price.
- `OrderItemInput.PortionId` optional. The server checks the portion belongs to the item and is active, takes its price, adds modifiers and packing as today, and freezes the fields. A wrong or inactive portion is a validation error naming the item.
  - Missing `PortionId` on an item with **one active portion** (or none defined beyond "Regular"): that portion is used, so old clients keep working for every existing item.
  - Missing `PortionId` on an item with **two or more active portions** is **refused** ("<Item> has several portions. Update the app to choose one."), never silently charged at the default. A stale client therefore cannot mis-bill; it can only fail loudly until it is updated.
  - The release raises the version to 0.1.2 and uses the existing minimum-client-version gate so old Windows apps are told to update; the web client reloads from the server; the Android APK is rebuilt and installed (delivery step 6).
- `OrderItemDto`, `BillItemDto`, `KitchenTicketItemDto`, `KotLine`, `InvoiceLine` gain `PortionName/PortionQuantity/PortionUnit` and `DisplayName`; `ItemName` is unchanged. Renderers (ESC/POS, PDF, browser KOT) and all list screens print `DisplayName`.
- Authorization: the same permission that edits menu items today; portions need nothing new. Audit entries for create/update include the portion list and price changes.

### Ordering screens
- **Web / Android (`OrderBuilderPage`, `MenuItemCard`):** an item with 2+ active portions shows portion chips (default selected, name over price); tapping a chip highlights it and the Add button reads "Add Half · 180". With modifiers, Add opens the existing options sheet with the portion already chosen and shown as the first row group (so an existing line can be edited). One-portion items keep today's single tap.
- **Windows (`OrderBuilderView`, `ModifierPickerViewModel`):** a tile of a multi-portion item opens the existing picker overlay with a Portion row group first, default selected, price updating. (My call: keeps the cashier screen's current tap-then-pick flow.)
- Cart merge key (`OrderComposer.MergeKey`, `CartItemViewModel.IsSameChoice`) includes `PortionId`. Saved drafts get optional portion fields; old drafts mean "default".
- **Sent lines are never re-priced** (they are not sent again; added dishes are new lines). **A draft is not a snapshot yet**: when a draft is re-saved or sent, each line is priced again from the current portion, exactly as the item price works today. If a draft line's portion has since been switched off or deleted, the save is refused with "<Item> (<Portion>) is no longer available. Choose another portion." Nothing is switched silently to the default. A loaded draft maps its line to a portion by `PortionId`, then by portion name; a line it cannot map is shown as needing a new choice.
- **Quantity and unit are labels only.** They show as a small caption on the chip ("500 GM"), in the admin table and in the frozen fields; they never change a price and never appear in `DisplayName` (which uses the portion name only), so there is no "1 KG (1 KG)".
- Availability / sold-out / low-stock indicators stay per item. Out-of-stock disables all portions.
- Cart shows "Chicken Biryani (Half)" with the unit price; totals, tax, discounts and the packing charge use the existing calculators.

### Admin (Windows)
- `MenuItemsPageView`: Portions & prices table (default radio, name, quantity + unit, price, on/off, remove, Add portion). A new item starts with one "Regular" row. Validation messages per row. The Price field is replaced by the table.
- The photo can be chosen while creating: it uploads right after the item is saved (today it is edit-only). Replace/remove keep working.
- Item list: picture, name, category, portion chips with prices (default marked, disabled struck through). Clicking a chip opens the item editor on that portion (no inline editing). (My call; mock-up showed inline edit, reduced for scope.)
- `MenuPreview` shows the portion chips as the waiter will see them.

### Reports and print
- `ReportQueries.ItemSalesAsync` groups by item name **and portion** (new Portion column, so Half and Full are separate rows; this matches how "(Parcel)" already separates); dashboard top items add all portions of an item together; category sales and totals are unchanged. Exports follow the DTO columns.
- Template preview sample lines show a portion; no template-layout change (the Item column prints `DisplayName`).

### Other readers of the price
Everything else that reads `MenuItem.Price` (seat bill split, import update, menu list sort, old clients) stays on the default portion's price. A re-import of an existing multi-portion item with a price changes the **default portion's price**, the same rule as an update, and shows in the import preview like any price change. Audit entries record each portion's old and new price in the existing audit JSON style.

## Delivery order (for the plan)
1. Domain + migration + menu API (portions CRUD inside create/update, menu DTOs, version bump).
2. Order path: factory, order/bill/kitchen/print/report DTOs, `DisplayName`, reports.
3. Windows admin (item editor, list, preview, photo while creating).
4. Web/Android ordering (chips, sheet, composer, drafts).
5. Windows order builder (picker, cart, drafts).
6. Docs, migration rehearsal, full test run, real-app check, Android APK rebuild.

## Testing (what proves it)
Domain: portion rules. Application: create/update with portions; price snapshot survives a later price edit; null portion = default; wrong/inactive portion rejected; one-portion item leaves old line shape; bill and KOT show `DisplayName`; packing, tax, discount on a portion line; stock takes 1 per unit. API: end-to-end order with two portions of one item. Client/bUnit: chips, highlight, price, cart merge. WPF: picker, editor validation, tile price. Migration rehearsal on a copy of real data (counts and checksums unchanged). Update the ~15 tests that assert on names. Manual run in the real Windows apps and the Android build.

## Left to the builder
Exact limits (name length, max portions 12 as proposed), the unit list wording, chip styling within the existing design system, SQL for the report change, and test fixtures.
