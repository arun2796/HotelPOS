# Audit: browser/Android Waiter and Kitchen vs the owner mock-ups (2026-10-17)

Planning only. Gap codes: **S** style-only,
**L** layout/new client component, **D** needs new server data, **X** excluded by the prompt, **O** owner decision.

**Overall:** the Kitchen board already has the mock-up structure; the waiter flow works but looks generic (solid tiles,
Unicode icons, no timeline). Most work is **S/L**; blockers are a few **D** items and the **O** conflicts in § 5.

## 1. Gap tables

### waiter-1-flow.png

| Panel · element | Exists today | Gap |
|---|---|---|
| 1 Greeting, avatar, bell | `MainLayout.razor` top bar | L |
| 1 "Waiter · Main Floor" | No link from a waiter to a section in any DTO | D → drop |
| 1 Filters All / Dine In / Pending | Section chips only (`TablesPage.razor`). No order type (D1 deferred); "Pending" is undefined | X (Dine In), O (Pending) |
| 1 Table card: code, guests, elapsed, #order | `TableDto.GuestCount/OccupiedAtUtc/CurrentOrderNumber` | S (pastel card instead of a solid tile) |
| 1 Stars and red count badge on cards | No data | O (badge could count unread bell entries for that table, client-side) |
| 1 "Reserved 7:00 PM" | `TableStatus` has no Reserved; no reservations | X/D → drop |
| 1 "+ New Order" and scan button | Ordering needs a table (`/tables/{id}/order`) | O |
| 2 Search with barcode icon | `#menu-search` searches name/code; no scanner | S; X for the scanner |
| 2 Picture cards with a star and a + button | `item-card`, `Host.ImageUrl(MenuEntryDto.ImageUrl)`, `item-star` | L. The demo menu has **no pictures** (placeholder 🍽) |
| 2 Cart bar "3 items · 1 guest · approx." | Sticky `.cart` with `Stepper` | L |
| 3 Cart page: thumbnails, steppers, modifier/note row | `cart-line` and note sheet | L |
| 3 "Order notes" (order level) | `CreateOrderRequest.Notes` / `PUT api/orders/{id}` exist; **the web never sends them** | L (wiring to an existing field) |
| 4 My Orders tabs Active / Completed / Held | Active only (`api/orders/active`, filtered to own `WaiterId`) | Completed: L (existing `GET api/orders?waiterId=&status=`); Held: X/O |
| 4 Progress Received→Cooking→Ready→Served | Status chip only | L when derived from `OrderStatus`; the **times** under each step are D on the list (no stage times in `OrderSummaryDto`) |
| 5 Details / Items / Timeline tabs | `TableDetailsPage.razor` (one card) | L |
| 5 Order type, Waiter, Ordered at, total | `OrderDetailDto` (no order type) | S; X for the type |
| 5 Print KOT | `GET api/print/kot/{id}` returns a document | O |
| 5 Edit order | Only guests/notes (`PUT api/orders/{id}`) and item cancel (`POST …/items/{id}/cancel`) are allowed once sent; neither is wired in `WaiterApi` | O/L |
| 5 Hold order | No status | X/O |
| 5 (missing in mock) SERVED, RELEASE TABLE | Present | **keep** (prompt § 2) |
| 6–8 Offline, error + Retry, back online | `OfflineBanner`, `ErrorState`, drafts | S (see § 6 Offline) |
| 9 Notifications page, Mark all read | `NotificationPanel` drop-down; `NotificationCenter.MarkAllRead` | L (new page) |
| 10 Settings: profile, appearance, **language** | `Settings.razor`, `ThemeToggle`; no localisation | S; Language X → drop |

### waiter-2-flow.png (only what differs from the first mock-up)

| Panel · element | Exists today | Gap |
|---|---|---|
| 1 Sign in: role toggle, terminal name, server online, illustration, show password | `SignIn.razor` (all but the illustration and the eye) | S/L |
| 2 Home: My tables / Ready / Bill request counts, quick actions, recent activity | `HomePage.razor` has the counts and lists | L (recent activity = `NotificationCenter`, this device only) |
| 5 Cart: Clear all, edit, delete per line | Decrease to 0 removes a line | L |
| 6 Add-ons "up to 3", spice "Choose 1", special instructions, qty | Options sheet (`OrderComposer.GroupRuleText`) + separate note sheet | L (merge). See conflict C1 |
| 7 Tabs All / Preparing / Ready / Billed | Data in `OrderStatus` | L |
| 8 Timeline: created, sent, accepted, preparing, ready | `CreatedAtUtc`, `SubmittedAtUtc`; ticket times through `GET api/kitchen/orders/{ticketId}` | L |

### kitchen-1-board.png and kitchen-2-board-detail.png

| Element | Exists today | Gap |
|---|---|---|
| Brand, "Kitchen Display", device name, user | `Shell.Set(...)`; `DeviceIdentity` name | L |
| Station chips **with counts** | Chips without counts (`KitchenPage.razor`) | L for "All stations". Per-station counts while one station is selected are stale: a joined station leaves `role:kitchen` (`docs/04` § groups) → O |
| Search order/table/item | — | L (client-side filter) |
| Left rail (mock 1) / navy admin sidebar with Analytics and Orders (mock 2) | Bottom nav | **X** (prompt: no admin sidebar). Completed/Station/Settings move into a header menu |
| KPI strip New / Preparing / Ready / Delayed / Completed today | Delayed chip + Completed today text | L (counts already computed) |
| Columns with subtitle and "Oldest first" sort | Columns sorted by `CreatedAtUtc` | L |
| DELAYED column (mock 2) | Derived by `KitchenRules.Timing`; no status | O (C2) |
| Ticket: big table code, station chip, #order · waiter · n items, elapsed | All in `KitchenTicketDto` (`OrderNumber`, `WaiterName`, `StationCode`) | S |
| Item pictures on tickets | `KitchenTicketItemDto` has no `MenuItemId` or `ImageUrl` | D |
| "No special instructions" / order notes | Order `Notes` is not on the ticket DTO (it is on the KOT document `OrderNotes`) | D on the board; L in the detail pane via `GET api/orders/{OrderId}` (any signed-in user) |
| Ready column "0 min" (time since ready) | `ReadyAtUtc`, `StartedAtUtc` exist | L (per-column timer; late still counts from `CreatedAtUtc`) |
| ACCEPT / START / MARK READY / DONE / RECALL | `KitchenRules.ActionsFor` | S (rename READY to "MARK READY") |
| VIEW / PRINT on tickets; "PRINT TICKET" | — | L (view); O (print) |
| Detail pane: items with prices, guests, subtotal/tax/total | Not on the ticket; prices on the kitchen contradict `KotShowPrices` | O |
| Detail timeline incl. "Taking longer than usual" | Ticket timestamps + `Timing` | L |

## 2. Components to build or restyle (Client.Shared/Shared)

| Component | Purpose | Used by |
|---|---|---|
| `Icon` (new) | Inline SVG line icons, replacing ⌂ ▦ ☰ ⚙ | everywhere |
| `AppHeader` (from `MainLayout`) | Waiter greeting/title + pill + bell + avatar | all waiter pages |
| `KitchenHeader` (new) | Brand, station chips with counts, search, clock, pill, bell, theme, menu, user/device | Kitchen, Completed, Station |
| `BottomNav` (from `MainLayout`) | Five-tab waiter nav (C3) | waiter |
| `StatusChip` | One status-to-colour/word map over `StatusLabels` | Tables, Home, My Orders, Details, Kitchen |
| `FilterTabs`, `KpiTile` | Pills with counts; number tiles | Tables, My Orders, Builder, Kitchen |
| `TableCard` | Pastel status card | Tables, Home |
| `MenuItemCard` | Picture, star, name, price, + | Builder |
| `CartBar`, `CartLine`, `QuantityStepper` (restyle `Stepper`) | Sticky summary, cart lines | Builder, Details |
| `BottomSheet` (wraps `.overlay/.sheet`) | One sheet pattern | options, note, cancel, release |
| `ItemOptionsSheet` | Groups + instructions + qty in one sheet | Builder |
| `OrderCard`, `KitchenProgress` | Order summary with four-step progress | My Orders, Home |
| `Timeline` | Timed events, late state | Details, Kitchen pane |
| `ActionList` | Outlined action rows, danger last | Details |
| `TicketCard`, `KdsColumn` | Extracted from `KitchenPage` | Kitchen |
| `TicketDetailPane` | Right-hand pane (if C6 approved) | Kitchen |

## 3. Data/API gaps (exact; nothing invented)

| Mock-up need | Missing | Workaround without a server change |
|---|---|---|
| Pictures on kitchen tickets | `KitchenTicketItemDto.MenuItemId` (or `ImageUrl`) | Map `OrderItemId`→`MenuItemId`→`ImageUrl` via `GET api/orders/{OrderId}` per ticket: N calls per refresh, **not recommended** |
| Order notes / guests on tickets | `KitchenTicketDto.OrderNotes`, `GuestCount` | Detail pane via `GET api/orders/{id}` |
| Stage times on My Orders cards | Stage timestamps on `OrderSummaryDto` / `OrderTicketDto` | Detail page: `GET api/kitchen/orders/{ticketId}` per ticket |
| Order type (Dine In/Takeaway/Delivery) | `Order.OrderType` (D1 deferred) | Omit |
| Held orders / Hold | No status, no endpoint | Omit, or show "Drafts on this phone" (`DraftStore`) |
| Per-station counts while one station is selected | Events are pushed per station group | Show counts only on "All stations", or poll |
| Print KOT / Print ticket from a browser | No KOT PDF; `window.print` is unsupported in Android WebView | `POST api/print/kot/{id}/copies` + browser print on desktop browsers only |

## 4. Proposed steps

Shared first: **0. Tokens and primitives (M)**: `wwwroot/css/tokens.css`, `app.css`, `Shared/Icon.razor`,
`StatusChip`, `BottomSheet`, `FilterTabs`. Align to #078747 / #F4F7FB / #172B4D / #64748B / #E2E8F0, add navy
#142235, ready = green. New CSS/JS files need links in both Web and Android `index.html`.

**Waiter track**

| # | Step | Files | Size |
|---|---|---|---|
| W1 | Shell: header, five-tab nav | `Layout/MainLayout.razor`, `Shared/AppHeader`, `BottomNav`; update `AccessibilityFollowUpTests` | M |
| W2 | Tables | `Pages/Waiter/TablesPage.razor`, `Shared/TableCard` | M |
| W3 | Home | `HomePage.razor`, `KpiTile` | S |
| W4 | Order builder + cart + options sheet + order notes | `OrderBuilderPage.razor`, `Services/OrderComposer.cs`, `DraftStore.cs` (draft gains `Notes`; versioned key), `MenuItemCard`, `CartBar`, `CartLine`, `ItemOptionsSheet`, `Stepper` | L |
| W5 | My Orders with tabs and progress | `MyOrdersPage.razor`, `WaiterApi.cs` (+list by waiter/status), `OrderCard`, `KitchenProgress` | M |
| W6 | Order details with tabs, timeline, actions | `TableDetailsPage.razor`, `WaiterApi.cs` (+`GetTicketAsync`, +`UpdateOrderAsync`, +`CancelItemAsync` if C4 approved), `Timeline`, `ActionList` | L |
| W7 | Notifications page, Settings/profile, Sign-in, offline/back-online | new `Pages/NotificationsPage.razor`, `Settings.razor`, `SignIn.razor`, `OfflineBanner.razor`, `NotificationPanel.razor` | M |
| W8 | Tests + gallery | `tests/HotelPOS.Client.Tests`, `docs/ui-ux/tools/web-flow.mjs`, `docs/ui-ux/web` | M |

**Kitchen track** (can run in parallel after step 0)

| # | Step | Files | Size |
|---|---|---|---|
| K1 | Kitchen shell: header, no bottom nav, header menu | `MainLayout.razor`, `Shared/KitchenHeader` | M |
| K2 | KPI strip, columns, sort, search | `Pages/Kitchen/KitchenPage.razor`, `Services/KitchenRules.cs` (stage timer, sort; keep late = from created), `KpiTile`, `KdsColumn` | M |
| K3 | Ticket card | `Shared/TicketCard` | M |
| K4 | Detail pane (if C6) | `TicketDetailPane`, `KitchenApi.cs` (+`GET api/orders/{id}`, +`GET api/menu` for pictures) | M |
| K5 | Completed and Station restyle | `CompletedPage.razor`, `StationPage.razor` | S |
| K6 | Tests + gallery | as W8 | S |

## 5. Conflicts for the owner

| # | Conflict | Recommendation |
|---|---|---|
| C1 | Mock-up 6 shows an add-ons/spice sheet, but the prompt says "Do not add a Modifiers & Add-ons interface". The app **already** lets waiters *choose* existing modifier groups (`OrderBuilderPage` picker); the exclusion is about *managing* them | Keep and restyle the existing choice sheet (prompt § 2 preserve); add no management screens. |
| C2 | Kitchen "Delayed" is a column in mock 2 (T01 appears in NEW **and** DELAYED) but no such status exists | KPI tile + red border + an optional "Delayed" filter that narrows the three columns. No fourth column and no duplicated cards |
| C3 | Nav: the prompt says Tables / My Orders / New Order / Notifications / Profile; the mock-ups show Home / Tables / Orders / Menu / More; today it is Home / Tables / My orders / Settings. "New Order" and "Menu" have no table context (route `/tables/{id}/order`) | New Order tab opens a "pick a table" sheet (available or own tables), then the builder. Drop the Menu tab. Owner picks the tab set |
| C4 | "Edit order", "Hold order" | Edit = guests/notes + cancel a line (existing endpoints). Hold: omit |
| C5 | Customers exclusion vs the existing desktop customer picker (`api/customers`, `billing.operate`) | No web conflict: the web client has no customer code. Leave the desktop alone |
| C6 | Kitchen detail pane shows prices/totals | Show items, guests, notes, timeline; prices off by default (matches `KotShowPrices`) |
| C7 | Print KOT on phone / Print ticket on kitchen | Omit on Android; desktop browsers only if approved (reserves copy numbers) |
| C8 | Palette: `tokens.css` mirrors the desktop Phase 17 tokens (#078B45, ready = **red** `--st-ready`) | Change web and desktop together, or accept drift |
| C9 | Item and ticket pictures | Pictures appear only after the owner uploads them; no stock photos (no mock data) |

## 6. Risks

- **Android WebView**: no `window.print`; old WebViews lack `:has()` (used in `app.css`) and container
  queries; test on a minSdk 26 device.
- **Offline**: there is no send queue (phase 19 left it out). The "Syncing pending orders" text must not suggest an
  automatic send. Drafts stay per phone; changing `DraftStore`'s shape needs a version and a migration path.
- **Service worker**: the published `service-worker-assets.js` still lists `app/_content/...` (401), and
  `/app/app/...` is open (phase 20 § 20.11). New CSS/icon/font files make the broken offline install worse. Fonts must
  be self-hosted or system fonts (LAN, no internet).
- **Licence seats**: every browser profile **and** every role on it is a device (`DeviceIdentity` keys per type:
  WEB-WAITER-x and WEB-KITCHEN-x), and `LicenseService` counts registered terminals. A kitchen monitor plus a tablet is
  two seats, and clearing site data creates a new one.
- **Realtime**: `MyOrdersPage` refreshes on every event without the 250 ms debounce the other pages use, so per-ticket
  timeline fetches would multiply calls.
- **Tests**: keep the bUnit selectors (`.item-card`, `#chip-favourites`, `#menu-search`, `.cart-line`, `#home-*`, `data-testid`) or update the tests together.
