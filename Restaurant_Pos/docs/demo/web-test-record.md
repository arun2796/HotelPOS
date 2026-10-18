# Web demo flow: test record

Run 2026-10-18T20:46:54.629Z against http://localhost:5000/app/ (Development API, owner's dev DB). Waiter on a 390x844 phone viewport, kitchen on a 1024x768 tablet viewport, installed Edge via Playwright. Main table O04, spare table DP1. Script: docs/ui-ux/tools/demo-web-flow.mjs. Screenshots: 02-waiter-phone/ (38) and 03-kitchen-tablet/ (15).

Set-up through the API (not screenshots): Requested table T04 is Billing (it has an open order of an earlier run); the main table is O04 (Outdoor) instead. Spare table T03 is not free; the offline drill uses DP1. Reservation #6 created for O04 (Meera Nair, party of 2, starts 02:44 am).

| # | Chapter | Step | Action | Expected | Result (Pass/Fail) | Screenshot | Notes |
|---|---|---|---|---|---|---|---|
| 1 | Waiter: sign in | Sign-in screen | Open http://localhost:5000/app/signin, pick Waiter, type waiter1 / password | Sign-in card with the Waiter role selected and the username filled | Pass | 02-waiter-phone/01-sign-in.png |  |
| 2 | Waiter: sign in | Home with KPIs | Tap Sign in | Home opens with the four KPI tiles and the quick actions | Pass | 02-waiter-phone/02-home-kpis.png |  |
| 3 | Waiter: tables | Tables in List mode | Bottom nav Tables, tap List in the List/Map toggle | List/Map toggle visible, table cards in a grid | Pass | 02-waiter-phone/03-tables-list.png |  |
| 4 | Waiter: tables | List shows the reservation badge | Filter the list to Outdoor | O04 card carries the reservation badge | Pass | 02-waiter-phone/04-tables-list-reservation-badge.png |  |
| 5 | Waiter: tables | Tables in Map mode (Ground Floor plan) | Choose All sections, tap Map, pick Ground Floor | The Ground Floor floor plan renders with table tiles | Pass | 02-waiter-phone/05-tables-map-ground-floor.png | 8 tiles on the plan |
| 6 | Waiter: tables | Map mode: section without a plan | Choose Outdoor in Map mode | The section is shown (plan if it has one, else the grid with a "No floor plan" hint) and the O04 badge is visible | Pass | 02-waiter-phone/06-tables-map-other-section.png | section has a plan |
| 7 | Waiter: tables | Back to All, List mode | Select All, tap List | The list is back for the order flow | Pass |  |  |
| 8 | Waiter: table & reservation | Table details O04 with the reservation | Open O04 from the list | Table details with the reservation banner (guest, party of 2, time window) and a Seat button | Pass | 02-waiter-phone/07-table-details-reservation.png |  |
| 9 | Waiter: table & reservation | Seat the reservation | Tap Seat on the reservation banner | The table becomes occupied and "New order" is offered | Pass | 02-waiter-phone/08-table-seated.png | seated through the reservation Seat button |
| 10 | Waiter: dine-in order | New order screen with the order type chips | Tap New order on the table | Order builder with the Dine-in / Takeaway / Delivery chips, Dine-in selected for the table | Pass | 02-waiter-phone/09-order-type-chips.png |  |
| 11 | Waiter: dine-in order | Search the menu | Type "biry" in the search box | Biryani results across categories | Pass | 02-waiter-phone/10-order-search.png |  |
| 12 | Waiter: dine-in order | Favourites | Star Chicken Biryani and Mutton Biryani, clear the search, open the Favourites chip | Both items appear under Favourites | Pass | 02-waiter-phone/11-favourites.png |  |
| 13 | Waiter: dine-in order | Item options sheet with a note | Tap Chicken Biryani, pick the options, a quick note and type a note | Options sheet with option groups, quick-note chips, the note text and the quantity | Pass | 02-waiter-phone/12-item-options-note.png |  |
| 14 | Waiter: dine-in order | Cart bar after adding three items | Confirm the biryani, add Butter Naan (Breads) and Masala Chai (Beverages) | Cart bar shows 3 lines | Pass | 02-waiter-phone/13-cart.png |  |
| 15 | Waiter: dine-in order | Review sheet | Tap Review order, raise the naan quantity, type order notes | Review sheet lists the lines with controls, guests, order notes and the Send button | Pass | 02-waiter-phone/14-review-sheet.png |  |
| 16 | Waiter: dine-in order | Send to kitchen | Tap Send to kitchen | Back on the table details with the order submitted and a success toast | Pass | 02-waiter-phone/15-sent-to-kitchen.png |  |
| 17 | Waiter: order details | Order details: Kitchen tickets and Print KOT | Scroll to the Kitchen tickets list | One ticket row per station, each with a Print KOT button | Pass | 02-waiter-phone/16-order-kitchen-tickets.png | 2 ticket(s) |
| 18 | Waiter: order details | Print KOT: button pressed | Tap Print KOT on the first ticket | A new tab opens with the ticket PDF | Pass | 02-waiter-phone/17-print-kot-pressed.png |  |
| 19 | Waiter: order details | Print KOT: the PDF tab | Look at the new tab | The KOT PDF is shown (blob: PDF url) | Pass | 02-waiter-phone/18-print-kot-pdf-tab.png | popup url blob:http://localhost:5000/3fde9fd3-9aa7… |
| 20 | Waiter: order details | Items tab with the note | Tap the Items tab | Lines with options, the note and the kitchen status | Pass | 02-waiter-phone/19-order-items.png |  |
| 21 | Waiter: takeaway order | Takeaway order builder (no table) | New order tab, pick Takeaway under "Channels (no table)" | Order builder titled Takeaway with the Takeaway chip selected | Pass | 02-waiter-phone/20-takeaway-builder.png |  |
| 22 | Waiter: takeaway order | Send the takeaway order | Add Gulab Jamun and Butter Naan, review, Send to kitchen | Sent: My orders opens with the takeaway TA-nnnn card | Pass | 02-waiter-phone/21-takeaway-sent.png | label TA-1027 |
| 23 | Waiter: held order | Save a draft from a new order | New order, Takeaway, add Masala Chai, Review, Save draft | A confirmation toast and My orders opens | Pass | 02-waiter-phone/22-draft-saved.png | toast: "Order #1053 held. Find it under My orders → Held." (re-run after the fix) |
| 24 | Waiter: held order | Held tab lists the saved draft | Open the Held tab | The draft just saved is listed with a Resume button | Pass | 02-waiter-phone/23-held-tab.png | 1 held order after the fix (re-run 2026-10-19) |
| 25 | Waiter: held order | Held tab with a server-side draft (set-up through the API) | POST /api/orders (Submit=false) as waiter1, reload My orders, open Held | The held takeaway order with a Resume button | Pass | 02-waiter-phone/24-held-tab.png | API draft order #1044 |
| 26 | Waiter: held order | Resume the held order | Tap Resume | The order builder opens as "Edit draft" with the held item in the cart | Pass | 02-waiter-phone/25-held-resumed.png |  |
| 27 | Waiter: held order | Send the resumed order | Review, Send to kitchen | Sent; My orders shows the order as active | Pass | 02-waiter-phone/26-held-resumed-sent.png |  |
| 28 | Waiter: orders | My orders (Active) | Open the Active tab | The dine-in order and both takeaway orders (TA-nnnn) are listed | Pass | 02-waiter-phone/27-my-orders.png |  |
| 29 | Waiter: orders | Takeaway order details | Open a TA-nnnn card in My orders | Details page for an order with no table: the TA label, tickets and Print KOT | Pass | 02-waiter-phone/28-takeaway-details.png | order TA-1027 with 1 ticket and Print KOT (re-run after the fix) |
| 30 | Waiter: bell, settings, theme | Bell panel | Tap the bell in the top bar | Notifications panel opens | Pass | 02-waiter-phone/29-bell-panel.png |  |
| 31 | Waiter: bell, settings, theme | Settings | Close the panel, More > Settings | Settings page | Pass | 02-waiter-phone/30-settings.png |  |
| 32 | Waiter: bell, settings, theme | Dark theme | Tables page, theme toggle to dark | Tables page rendered in the dark theme | Pass | 02-waiter-phone/31-tables-dark.png | body background rgb(15, 22, 35) |
| 33 | Kitchen: sign in | Sign-in screen | Open the sign-in, pick Kitchen, type kitchen1 / password | Sign-in card with the Kitchen role selected | Pass | 03-kitchen-tablet/01-sign-in.png |  |
| 34 | Kitchen: board | Board with KPI strip and the new tickets | Tap Sign in | The board shows the New/Preparing/Ready KPI strip, the O04 dine-in ticket and the takeaway ticket(s) with the Counter chip | Pass | 03-kitchen-tablet/02-board-new-tickets.png | 2 ticket(s) for O04, 12 takeaway ticket(s) |
| 35 | Kitchen: board | The O04 and takeaway tickets in the New column | Scroll the board to the new tickets (the board also holds older tickets of the dev DB) | Both new tickets are in view | Pass | 03-kitchen-tablet/03-board-new-tickets-scrolled.png |  |
| 36 | Kitchen: board | Ticket detail pane with item pictures | Open the newest New ticket that has picture items | Right pane with guests, lines with real pictures, Print KOT and the progress timeline | Pass | 03-kitchen-tablet/04-ticket-detail-pictures.png | picture(s) loaded from /images/menu (re-run after the fix, no DOM rewrite) |
| 37 | Kitchen: board | Takeaway ticket detail | Close the pane, open the takeaway ticket | Detail pane with the Counter / TA channel badge | Pass | 03-kitchen-tablet/05-ticket-detail-takeaway.png |  |
| 38 | Kitchen: station | Station page: Show item pictures on | Close the pane, Station in the top bar, tick "Show item pictures on tickets" | The checkbox is ticked | Pass | 03-kitchen-tablet/06-station-show-pictures.png |  |
| 39 | Kitchen: board | Board with thumbnails on the cards | Back to the Kitchen board with Show item pictures on | Ticket cards show the item pictures | Pass | 03-kitchen-tablet/07-board-thumbnails.png | 27 thumbnails loaded (re-run after the fix, no DOM rewrite) |
| 40 | Kitchen: cooking | Accept the O04 ticket(s) | Press Accept on every ticket of the table | Tickets show the Accepted chip and a Start button | Pass | 03-kitchen-tablet/08-accepted.png | 2 ticket(s) |
| 41 | Kitchen: cooking | Start preparing | Press Start | Tickets move to Preparing with a Ready button | Pass | 03-kitchen-tablet/09-preparing.png |  |
| 42 | Kitchen: cooking | Mark ready | Press Ready | Tickets move to the Ready column | Pass | 03-kitchen-tablet/10-ready.png |  |
| 43 | Kitchen: completed, bell, theme | Completed list | Top bar > Completed | Completed tickets page | Pass | 03-kitchen-tablet/11-completed.png |  |
| 44 | Kitchen: completed, bell, theme | Bell panel | Back to the board, tap the bell | Notifications panel opens | Pass | 03-kitchen-tablet/12-bell-panel.png |  |
| 45 | Kitchen: completed, bell, theme | Dark theme | Close the panel, theme toggle to dark | Board in the dark theme | Pass | 03-kitchen-tablet/13-board-dark.png |  |
| 46 | Waiter: ready, serve, bill | My orders shows Ready | Bottom nav Orders | The O04 order card has the Ready status | Pass | 02-waiter-phone/32-my-orders-ready.png |  |
| 47 | Waiter: ready, serve, bill | Mark served | Open O04, tap Mark served | The Mark served action disappears and the order is served | Pass | 02-waiter-phone/33-served.png |  |
| 48 | Waiter: ready, serve, bill | Request the bill | Tap Request bill | Banner "Bill requested"; the bill waits for the cashier (desktop chapter) | Pass | 02-waiter-phone/34-bill-requested.png |  |
| 49 | Waiter: ready, serve, bill | Bell with the ready notification | Tap the bell | Notification list shows the order-ready entry | Pass | 02-waiter-phone/35-bell-ready.png |  |
| 50 | Waiter: offline | Offline banner | Open an empty Takeaway order builder (no table needed), drop the network | Offline banner is shown | Pass | 02-waiter-phone/36-offline-banner.png |  |
| 51 | Waiter: offline | My orders while offline | Open My orders while offline | Banner or an error state, no crash | Pass | 02-waiter-phone/37-offline-my-orders.png |  |
| 52 | Waiter: offline | Back online | Restore the network | The offline banner goes away | Pass | 02-waiter-phone/38-back-online.png |  |

**Summary: 52 passed / 0 failed / 52 total** (re-run on 2026-10-19 after the fixes in main f382716: the 4 earlier Fail steps now pass, the 2 workaround steps are removed; 03-kitchen-tablet screenshots renumbered).

## Device clean-up

- WEB-KITCHEN-4F5B39 left active (not this run's profile)
- WEB-KITCHEN-7F9C22 (Kitchen) deactivated
- WEB-KITCHEN-EA7A9A (Kitchen) deactivated
- WEB-WAITER-31D9CD (Waiter) deactivated
- WEB-WAITER-7E936E (Waiter) deactivated
- WEB-WAITER-887240 left active (not this run's profile)

## UI/UX issues noticed (measured by the script)

- Console errors / page errors: 316 (see web-console-errors.json).
- Screens with a visible control under 48 px: 9 of 52 (see web-small-targets.json).
  - 02-waiter-phone/03-tables-list.png: List 45x48
  - 02-waiter-phone/04-tables-list-reservation-badge.png: List 45x48
  - 02-waiter-phone/05-tables-map-ground-floor.png: List 45x48; T01 60x44; T02 60x44; T03 60x44 (+5)
  - 02-waiter-phone/06-tables-map-other-section.png: List 45x48; O01
2 71x46; O02
2 71x46; O03
2 71x46 (+1)
  - 02-waiter-phone/28-takeaway-details.png: Go to the start 101x19
  - 02-waiter-phone/29-bell-panel.png: Go to the start 101x19
  - 02-waiter-phone/30-settings.png: checkbox 20x20; checkbox 20x20; checkbox 20x20; checkbox 20x20 (+8)
  - 02-waiter-phone/31-tables-dark.png: List 45x48
  - 03-kitchen-tablet/06-station-show-pictures.png: checkbox 22x22

## UI/UX issues noticed (by eye)

> Re-run note: issues 1, 2 and 3 below were fixed in main f382716 (ImageUrl keeps only http(s) absolute URLs; /orders/{id} shows a table-less order with tickets and Print KOT; Save draft creates a server Draft) and verified in the re-run. They are kept for the history. Still open: 4 to 8.

1. **Item pictures never load in the browser build (Phase 22 P5 broken in the PWA).** `ClientHostExtensions.ImageUrl` calls `Uri.TryCreate("/images/menu/x.jpg", UriKind.Absolute, ...)`, which succeeds for a rooted path on the WASM/Unix runtime, so every `src` becomes `file:///images/menu/x.jpg` and the browser refuses it ("Not allowed to load local resource", most of the 316 console errors). Seen as broken-image icons on the order builder menu cards and cart thumbnails (02-waiter-phone/13-cart.png, 14-review-sheet.png), and as hidden or empty thumbnails in the kitchen before the fix. Fix: accept only `http`/`https` absolute URIs (`absolute.Scheme is "http" or "https"`). After the fix the real pictures show in 03-kitchen-tablet/04-ticket-detail-pictures.png and 07-board-thumbnails.png.
2. **Takeaway/delivery order details page does not exist from My orders.** Tapping a `TA-nnnn` card opens `/orders/{id}`, `TableDetailsPage` sees an order without a table and redirects to `waiter/orders`, a route that is not defined, so the phone shows "This page does not exist." (02-waiter-phone/28-takeaway-details.png). A takeaway order's Kitchen tickets list and Print KOT button are therefore unreachable on the phone (step 29 Fail).
3. **"Save draft" on a new order does not create a held order.** It keeps the cart only on the phone ("Draft kept on this phone."), while the Held tab lists server-side drafts, so the Held tab stays empty (02-waiter-phone/22-draft-saved.png, 23-held-tab-empty.png; step 24 Fail). The held-tab and resume screenshots (24-26) use a draft created through `POST /api/orders` with `Submit=false`.
4. **Tables Map mode on a phone** (02-waiter-phone/05-tables-map-ground-floor.png): the Ground Floor plan is a small strip of tiles at the top with the rest of the screen empty, and the right-most tile (T05) is clipped at the edge so the plan scrolls sideways; the tiles are 60x44 px (under the 48 px target).
5. **Table codes starting with the letter O read as zero** in the kitchen card font ("O04" looks like "004", 03-kitchen-tablet/03-board-new-tickets-scrolled.png).
6. **Kitchen board is crowded by stale tickets**: with older dev-DB tickets (59 h old, all red) the new tickets sit far down the New column (03-kitchen-tablet/02-board-new-tickets.png), so the board screenshot needs a scroll (03-board-new-tickets-scrolled.png).
7. **Controls under 48 px** on several screens: List/Map toggle (45x48), table tiles in Map mode (60x44 / 71x46), the checkboxes on Settings (20x20) and the station page "Show item pictures" checkbox (22x22).
8. **Print KOT PDF tab** (02-waiter-phone/18-print-kot-pdf-tab.png) opens in the 390 px phone viewport and the 58 mm ticket renders very small and low-contrast in the viewer; readable only after zooming.

## Run notes

- The requested main table T04 (and T03) already had open orders of earlier runs, so the script used the first free table not reserved for the desktop session: the main table is **O04** (Outdoor, reservation #6 for Meera Nair), `EXCLUDE_TABLES` defaults to T05. An earlier, discarded run took T05 for a moment while the desktop session had released it; that order (Billing, requested by waiter1) is left for the cashier.
- Earlier runs of this script left finished demo orders (dine-in with a bill requested, takeaway tickets TA-1027 and similar, a draft) on tables T05, T14, O01, O02, O03 and in the kitchen board; none of the owner's earlier data was touched. Demo pictures were uploaded for Chicken Biryani, Butter Naan, Masala Chai and Gulab Jamun (`DELETE /api/menu-items/{id}/image` removes them).
- The host's TCP ports were exhausted several times by another session (thousands of connections from a test host to :5000, ERR_ADDRESS_IN_USE / ERR_NO_BUFFER_SPACE); the script retries such steps after 30 s.
