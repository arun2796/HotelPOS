# 12 — Portion pricing (Quarter / Half / Full...) and the new item form: release notes, upgrade and rollback

Version **0.1.2**. One database migration (`Phase32_ItemPortions`). Read § 2 and § 4 before upgrading.

## 1. What changes for the restaurant

| Area | Change |
|---|---|
| Menu (Windows admin) | The item form's single **Price** is replaced by **Portions & prices**: 1 to 12 rows (Quarter, Half, Full, 1 KG, 500 GM, 250 ML...), each with its own price, an optional quantity + unit label, a default, and an On switch. The photo can be chosen while the item is new. The list shows each item's picture, its portions and prices. |
| Ordering — web, Android | An item with two or more portions on sale shows chips on its card, the default selected; tapping a chip changes the price on the **Add** button ("Add Half · 180.00"). Two portions of one dish are two cart lines. An item with one price works exactly as before (one tap). |
| Ordering — Windows (captain, cashier) | A tile of such an item opens the existing picker with a **Portion** group first. |
| Kitchen, KOT, bill, invoice | The portion is printed with the dish: `Chicken Biryani (Half) × 2`, `Chicken Biryani (Half) (Parcel)`. Tax, discount, packing charge and modifiers work as before; packing is charged per unit once, on the portion's price. |
| Prices of past orders | The portion and its price are **frozen on the order line and the bill line**. Changing a price later never changes an order, bill or invoice already made. |
| Reports | **Item sales** has a **Portion** column after the item name (Half and Full are separate rows; blank for items with one price and for sales before this release). The dashboard's top items add all portions of an item together. CSV/Excel exports gain the same column, so column positions after "Item name" move by one. |
| Stock | Unchanged: every portion sold takes **1** off the item's count (Exact and Approximate modes as before). |

Not in this release: a portions column in the Excel menu import (a price still imports as the item's one "Regular" portion; re-importing an item with portions changes its **default** portion's price), per-portion pictures, stock or tax.

## 2. Stale apps

An order line for an item with **two or more portions on sale** that does not name a portion is **refused** ("<item> has several portions. Update the app to choose one."); it is never charged at the default. Items with one price keep working in old apps.
`MinClientVersion` is raised to **0.1.2** in `appsettings.json`, so a Windows app older than 0.1.2 shows "Update required" at sign-in. The web client reloads from the server. **Rebuild and reinstall the Android APK** (`09-android-app.md`).

## 3. Database changes

| Migration | Change | Data risk |
|---|---|---|
| `Phase32_ItemPortions` | New table `MenuItemPortions`; one **Regular** default portion per existing item at its current price; new nullable columns `OrderItems.PortionId/PortionName/PortionQuantity/PortionUnit` and `BillItems.PortionName/PortionQuantity/PortionUnit`. | No existing row is rewritten. Old orders and bills keep `NULL` portion columns and read exactly as before. |

Deleting a portion keeps order lines: the foreign key is `ON DELETE SET NULL` and the name, quantity, unit and price stay on the line.

### Rehearsal on a copy of real data (2026-11-25)
A `pg_dump -Fc` of the development database taken before it was cleared (11,362 order items, 5,635 bills, 11,325 bill items, 19 menu items, at `Phase31_SeatGroupTickets`) was restored into a scratch database and migrated up, down to Phase 31 and up again:

* Row counts and an MD5 over ids, names, prices and totals of **order items, bills, bill items and menu items were identical** before the upgrade, after it and after the rollback.
* After the upgrade: 19 portions, every one `Regular`, default, at its item's price; 0 order lines and 0 bill lines carry portion data.
* After the rollback the table and all seven columns are gone and the data is unchanged; the second upgrade gave the same result.
* The scratch database was dropped afterwards.

## 4. Backup, upgrade, rollback

**Before:** take a backup (Administration → Backup) and copy it off the server (`03-backup-and-recovery.md`). The API also writes its own verified `pg_dump` before it migrates.

**Upgrade:** quiet period; run the new server installer over the old one (`04-upgrade.md`); check `/health` and the top of the log for one "Applying migration `Phase32_ItemPortions`". Update every Windows terminal (the app offers the update; otherwise run `HotelPOS-Client-Setup.exe`), reload the web pages (Ctrl+F5), and install the new Android APK.

**Rollback — restore (safest):** stop the service, install the previous server, restore the pre-migration dump (`pg_restore --clean --if-exists --no-owner -h localhost -U postgres -d hotelpos <file>`). Orders entered after the upgrade are lost with a restore.
**Rollback — keep the new data:** with the service stopped, `dotnet ef database update 20261124184559_Phase31_SeatGroupTickets --connection "<connection string>"`. This removes the portions table and the portion columns; orders and bills are not touched, but the portion names printed on them (`Half`) are no longer stored. Then install the previous server and set `MinClientVersion` back.

## 5. Manual test procedure (run on a real terminal before go-live)

1. **Admin:** Menu → Items → New item. Choose a picture. Name "Chicken Biryani". Portions: Quarter 100 (250 GM), Half 180 (500 GM, default), Full 320, 1 KG 550 switched off. Create. The picture and the four chips appear in the list ("1 KG 550" struck through).
2. **Waiter (phone):** open a table → New order. The card shows Quarter / Half / Full (no 1 KG), Half selected, "Add Half · 180.00". Tap Quarter: "Add Quarter · 100.00". Add, then Full, then Add. Cart: `Chicken Biryani (Quarter)` and `(Full)`, total 420.00.
3. **Windows cashier:** a tile of the same dish opens the picker; Portion row first; choosing Quarter changes the price; confirm. A one-price dish still adds in one tap.
4. **Send.** Kitchen display and the printed KOT read `Chicken Biryani (Quarter) × 1`.
5. **Request bill, discount 10 %, pay.** The invoice line reads `Chicken Biryani (Quarter) | 1 | 100.00 | 100.00`; GST and the discount are on the portion's price.
6. **Change the price** of Quarter to 150. Open the bill and invoice from step 5: still 100.00. A new order is 150.00.
7. **Parcel:** a parcel of the Half reads `Chicken Biryani (Half) (Parcel)`, price 180 + the packing charge.
8. **Stale app:** on an app older than 0.1.2 the sign-in shows "Update required".
9. **Reports:** Reports → Item sales shows Quarter and Full as separate rows with a Portion column.

## 6. Known limitations

* **Thermal printing is not verified on hardware** and the Android app was not run on a physical phone for this change (see the rehearsal and test counts in the project ledger).
* A draft saved before this release that holds a dish now sold in portions is flagged "choose a portion again" (web) or "remove the line and add it again" (Windows); it is never sent with a guessed portion.
* The Excel menu import has no portions column yet.
* The word "portion" also appears in the kitchen stock screens ("portion quota"); it means a counted unit there, not a priced portion.
