# Web client (waiter + kitchen) UI/UX gallery — Phase 21.5

**Re-captured 2026-10-23 (Phase 25 pass 4)** on `main` against the Development API on `http://localhost:5000/app`: table cards
with a tone stripe instead of a pastel fill, the dot-and-hairline status badges and connection pill, and the theme
switch moved to the More sheet on waiter phones. Same script, same viewports; the notes below still describe each screen.

Captured **2026-10-17** on branch `phase-21-role-ui-ux` against the API in Development mode on
`http://localhost:5100/app` (demo seed, same dev database as the Phase 20 runs, so the day-old orders #1003/#1005/#1009/#1014
are still on the boards), headless Microsoft Edge through Playwright 1.58, `deviceScaleFactor: 2`. Waiter: phone 390 × 844
(touch, mobile). Kitchen: tablet 1024 × 768 landscape. Accounts `waiter1` and `kitchen1`. Order **#1016 on T04** (Chicken
Biryani with options and a note, 2 × Butter Naan, Masala Chai, order note "Birthday table — bring the dessert last") was
created by this run, accepted → started → marked ready by the kitchen, marked served and left with the **bill requested**.

Script: [`../tools/web-flow.mjs`](../tools/web-flow.mjs) (how to run it is in its header). Its selectors now follow the
Phase 21 markup (`data-action`, `data-testid`, the five-tab shell, the sheets, the kitchen head nav). Besides the images it
writes `small-targets.json` (visible controls under **48 px** in either dimension, the Phase 21 target), `console-errors.json`
and `page-overflow.json` (any screen whose layout is wider than the phone — see issue 1).

Mock-ups compared: `docs/design/reference/waiter-1-flow.png`, `waiter-2-flow.png`, `kitchen-1-board.png`,
`kitchen-2-board-detail.png`. Plan: `docs/phases/phase-21-role-ui-ux-redesign.md` (§ 3 Out, § 4 decisions D1–D11).

## Screenshots

| Image | What it shows | Viewport |
|---|---|---|
| `waiter/01-waiter-sign-in.png` | Sign in (W7): hero mark, restaurant name, Waiter / Kitchen segmented control, eye toggle, terminal + server badge | phone |
| `waiter/02-waiter-home.png` | Home (W1): four KPI tiles (My tables, Open orders, Ready to serve, Bills requested), quick actions, Needs attention, activity | phone |
| `waiter/03-waiter-tables.png` | Tables (W2): section chips with counts, "12 available · 4 in use", By section / Available first, pastel `TableCard`s with timer, status badge, guests, order # | phone |
| `waiter/03b-waiter-pick-table.png` | "New order" tab → pick-a-table sheet (D5): Available grid, then "Your active tables" | phone |
| `waiter/04-waiter-table-details.png` | T04 available: Seat guests stepper "2 guests" + Occupy | phone |
| `waiter/04b-waiter-table-occupied.png` | T04 after Occupy, reloaded by itself; `ActionList` with New order / Release table | phone |
| `waiter/05-waiter-order-search.png` | Order builder (W4): search "biry", no chip highlighted, picture cards (initials placeholder, D11), two items pinned | phone |
| `waiter/06-waiter-favourites.png` | ★ Favourites chip with the two pinned biryanis | phone |
| `waiter/07-waiter-item-options.png` | `ItemOptionsSheet` (D2): Add-ons, Spice level, quick-note chips, special instructions, quantity stepper, Add to order — one sheet | phone |
| `waiter/08-waiter-cart.png` | Sticky `CartBar`: "Order · 3 items", guests stepper, thumbnails with quantity badges, approx. total, Review order; toast "Masala Chai added." | phone |
| `waiter/08b-waiter-review-sheet.png` | Review sheet (confirm-before-send): lines with − / + / edit / delete, Add more items, guests, order notes, Save draft / Send to kitchen | phone |
| `waiter/09-waiter-after-send.png` | Straight after Send: Order #1016 "Sent to kitchen", Details tab, order notes in amber, Actions (Add items, Edit order, Cancel order last) | phone |
| `waiter/09c-waiter-order-items.png` | Items tab: lines with options/note, per-line ticket status chip, Cancel line | phone |
| `waiter/09d-waiter-order-timeline.png` | Timeline tab (W6): `OrderTimeline` steps + event list with kitchen times | phone |
| `waiter/09b-waiter-my-orders.png` | My orders (W5): Active 5 / Completed tabs, "ready ones first", `OrderCard`s with the four-step progress | phone |
| `waiter/09e-waiter-my-orders-completed.png` | Completed tab (today, newest first) | phone |
| `waiter/10-waiter-bell-panel.png` | Bell drop-down (line icons per kind) | phone |
| `waiter/10b-waiter-more-sheet.png` | More sheet (D5): profile + theme, device status card, notifications, Settings, Sign out | phone |
| `waiter/11-waiter-settings.png` | Settings: profile, Appearance, This device, notification kinds with Pop-up / Sound | phone |
| `waiter/12-waiter-tables-dark.png` | Tables in dark theme | phone |
| `kitchen/13-kitchen-sign-in.png` | Sign in, Kitchen selected | tablet |
| `kitchen/14-kitchen-board-new.png` | Kitchen Display (K1–K3): station chips, search, KPI strip (New / Preparing / Ready / Delayed / Completed today), Delayed + Oldest first toggles, NEW · PREPARING · READY columns with "oldest" timers, red late border, T04 BAR ticket #1016 | tablet |
| `kitchen/14b-kitchen-ticket-detail.png` | `TicketDetailPane` (K4) as a right drawer: status, station, elapsed, guests, order note, items, Progress timeline | tablet |
| `kitchen/14c-kitchen-delayed-filter.png` | Delayed filter on (D4): the three columns narrowed to late tickets, no fourth column | tablet |
| `kitchen/15-kitchen-station.png` | Station page through the head nav | tablet |
| `kitchen/16a-kitchen-accepted.png` | T04 tickets accepted ("Accepted" chip, START solid) | tablet |
| `kitchen/16-kitchen-preparing.png` | T04 in PREPARING with MARK READY; note in bold amber | tablet |
| `kitchen/17-kitchen-ready.png` | T04 in READY with DONE (solid) / RECALL (outlined) | tablet |
| `kitchen/18-kitchen-completed.png` | Completed page (back link instead of the station chips) | tablet |
| `kitchen/19-kitchen-bell-panel.png` | Kitchen notification panel | tablet |
| `kitchen/20-kitchen-dark.png` | Kitchen Display, dark theme | tablet |
| `waiter/21-waiter-my-orders-ready.png` | My orders: #1016 first with the green Ready chip and three steps done | phone |
| `waiter/22a-waiter-table-ready.png` | Order #1016 Ready: actions Add items, Edit order, Mark served, Request bill, Cancel order | phone |
| `waiter/22b-waiter-table-served.png` | After Mark served (D9): status Served, Mark served gone | phone |
| `waiter/22-waiter-bill-requested.png` | After Request bill, reloaded by itself: "Bill requested — the counter has it." | phone |
| `waiter/23-waiter-bell-ready.png` | Bell: order ready / being prepared / kitchen accepted for T04 | phone |
| `waiter/24-waiter-offline-banner.png` | Network off on an empty builder for T02: offline banner with the "Reconnecting…" pill (W7) | phone |
| `waiter/25-waiter-offline-my-orders.png` | My orders while offline: "The server cannot be reached… Retry" | phone |
| `waiter/24b-waiter-back-online.png` | Back online: pill Connected, "Back online." notice, list reloaded by itself | phone |

Removed (the screen no longer exists): `07b-waiter-kitchen-note` (the note lives in the options sheet, D2),
`08b-waiter-cart-fullpage` (the cart is a sticky bar; the lines are in the review sheet), `14b-kitchen-board-new-scrolled`
(replaced by the detail pane and the Delayed filter).

## Comparison with the mock-ups

| Screen (image) | Mock-up | Matches | Differs | Gap by design |
|---|---|---|---|---|
| Tables (03, 12) | waiter-2 #3, waiter-1 #1 | Section chips with counts, pastel cards in a 3-column grid, code, timer, status, guests/seats, order #, green Ready, orange Billing, five-tab bar with the raised New order | "Bill requested" now wraps instead of being cut (issue 2, fixed); the mock-up's "Reserved" tile has no data here; the mock-up draws free tables plain, the app badges them "Available" | Ready green / Billing orange per D8 |
| Order builder (05–08) | waiter-2 #4, waiter-1 #2 | Search, Favourites + category chips, 2-column picture cards with ★ and +, price, sticky "Order · n items" bar with guests stepper, thumbnails with quantity badges, approx. total, one primary button | Initials stand in for the photos (the demo seed has no `ImageUrl`); ("+ options" hint fixed, issue 3; the toast now sits at the top, issue 4); the button says "Review order" (mock-up "View Order" / "Send to Kitchen" — the send is in the review sheet) | No stock photos, owner uploads pictures (D11); no barcode/scan button (not in scope) |
| Item options (07) | waiter-2 #6 | Add-ons (optional, up to n), Spice level (choose 1), special instructions with quick chips and free text, quantity stepper, Add to order, price under the title | Quick-note chips are now 48 px (issue 6, fixed) | Modifier *management* is out (§ 3, D2) |
| Review / cart (08b) | waiter-1 #3, waiter-2 #5 | Lines with − / + / edit / delete, options and note under each line, Add more items, Order notes (for the kitchen), approx. total, Send to kitchen | Presented as a bottom sheet instead of a page; no "Items (n) / Notes & Info" tabs | — |
| Order details (09, 09c, 09d, 22a) | waiter-1 #5, waiter-2 #8 | Order # with status chip, Details / Items / Timeline tabs, detail list (table, guests, waiter, ordered at, items, approx. total, notes), outlined `ActionList` rows (Add items, Edit order, Mark served, Request bill, Cancel order last in red) | Top-bar title now fits beside the back button (issue 5, fixed); toast now at the top (issue 4) | No Hold order, no Print KOT (D9) |
| My orders (09b, 21) | waiter-1 #4, waiter-2 #7 | Active (count) / Completed tabs, "ready ones first", cards with # · table, status chip, elapsed, four-step Received → Preparing → Ready → Served, items · guests, approx. total; Ready first with a green chip | No clock values under the steps on the list (the list DTO carries no stage times; the details Timeline has them); no search icon; subtitle is a single ellipsis line (issue 5, fixed) | No "Held" tab (Hold omitted, D9) |
| Kitchen board (14, 14c, 16, 17, 20) | kitchen-1, kitchen-2 | Station chips (All stations with count), search, KPI strip with the red Delayed tile, Oldest first, NEW / PREPARING / READY with subtitles and oldest timers, tickets with table code, station chip, red elapsed chip and border when late, "n × item", options, amber note, ACCEPT solid / START outlined, MARK READY, DONE / RECALL, clock and connection pill in the bar | Navigation is four icons in the top bar at 1024 px (labels appear from 1100 px) instead of the mock-up's left sidebar; no item pictures on tickets; no per-column sort menu (one board-wide Oldest first toggle); the mock-up's "Analytics" and "Orders" entries do not exist | Delayed is a KPI + red border + filter, not a fourth column (D4); ticket pictures deferred (D11) |
| Kitchen detail (14b) | kitchen-2 right pane | Right drawer with "T04 · order #1016", ticket number, waiter, status + station chips, elapsed, guests, order note in amber, Items with options/notes, Progress timeline with the received time | No action buttons in the pane (ACCEPT/START/MARK READY stay on the card); no picture per item | No prices, subtotal, tax or total in the kitchen (D9); Print ticket deferred (D9) |

Summary: 8 screens compared — all 8 match the mock-up's structure and words; 6 have visible differences listed above
(2 are cosmetic defects fixed or open below, the rest are smaller choices); 7 carry gaps that the plan decided (D2, D4, D8,
D9, D11).

## Issues found while driving

Paths are under `src/HotelPOS.Client.Shared/`. Target sizes from `small-targets.json` (48 px, the Phase 21 rule; the
older 44 px rule in `wwwroot/css/app.css` is noted where it still passes).

| # | Severity | State | Where | Issue |
|---|---|---|---|---|
| 1 | **High** | **Fixed in this run** | `wwwroot/css/waiter.css:24` (`.table-card-head`) | A table occupied for more than ten hours shows "26 h 44 min"; the `white-space: nowrap` timer beside the 22 px code was wider than a 3-column card and overflowed it, making the whole Tables page 404 px wide on a 390 px phone. Mobile Chromium then zooms the page out and tap coordinates no longer line up: the first script run could not tap the "New order" tab at all (Playwright: "nav.bottomnav intercepts pointer events"). One line: `flex-wrap: wrap`, the timer drops under the code. The script now records any such overflow in `page-overflow.json` (empty after the fix). |
| 2 | Medium | **Fixed (21.5 follow-up)** | `wwwroot/css/waiter.css` (`.table-card .status-badge span`) | "Bill requested" wrapped to two lines in the 3-column card instead of being cut (03); label never dropped. |
| 3 | Low | **Fixed** | `wwwroot/css/waiter.css` (`.item-meta`) | The "+ options" hint wraps and the meta row keeps 40 px clear of the + button (05, 08). |
| 4 | Low | **Fixed** | `wwwroot/css/app.css` (`.shell.waiter .toasts`; no `:has()` anywhere in Client.Shared) | On every waiter page the toast now sits at the top under the bar, so it never covers the cart bar, an action row on Order details, or the bottom nav. Kitchen keeps its bottom toast. Verified on the builder (08); the details shot 22 predates the fix. |
| 5 | Low | **Fixed** | `wwwroot/css/app.css` (`.topbar .title`, `.sub`, `.conn`) | Subtitle is one ellipsis line, the title block keeps 48 px, and on phones the *Connected* pill is a green dot (title attribute keeps the words; Reconnecting/Offline keep their text). "Order #1021" now shows in full; the builder's "T02 · New order" still ends in an ellipsis. |
| 6 | Low | **Fixed** | `wwwroot/css/app.css`, `waiter.css` | Segmented, search Clear, search input, quick-note chips, option buttons, cart line buttons and Cancel line are now 48 px (`--touch`). `small-targets.json` lists only the 20 px settings checkbox (known). |
| 7 | Low | **Fixed** (label; needs the WASM rebuild to show) | `Pages/Kitchen/TicketCard.razor:75` | A Ready ticket now reads "Ready · waiting 26 h 37 min" (time since `ReadyAtUtc`). The waiter cards show time since occupied / ordered and are unchanged. |
| 8 | Info | Open (Phase 20 issue 6) | — | Each new browser profile registers a terminal and uses a licence seat. This session registered 3 new terminals (waiter and kitchen browser identities — now kept in `%TEMP%\hotelpos-web-flow` for reruns — and one `PROBE` terminal from a curl check); the licence shows 10 of 25 in use. Deactivate the `WEB-*` / `PROBE` devices afterwards. |

Console: one `401` per role on the sign-in page (a resource fetched before the token exists; no visible effect) and four
`ERR_INTERNET_DISCONNECTED` during the offline drill (expected). No page errors, no "Something went wrong" bar.

### Worked well

Every step of the flow ran end to end without a "tap again" nudge (the Phase 20 toast exception is gone). Occupy, Send,
Mark served and Request bill reload the page by themselves; the kitchen detail pane fetches guests and the order note; the
Delayed filter narrows the columns; the offline banner, error state with Retry and "Back online." notice all appear
without a tap; the dark theme covers every element seen.

## Not captured

- "Claimed" notice: no cashier claimed the bill while the flow ran.
- A ticket pressed DONE: the flow leaves T04 served with the bill requested, so the Completed page (18) lists earlier tickets only.
- Item photos: the demo seed has no `ImageUrl`, so every card shows initials.
