# HotelPOS - Menus, Screens and Fields

This is the map of the HotelPOS desktop application for the restaurant owner, testers and trainers. It lists every menu in the left-hand sidebar, every screen (sub-menu) under it, and the fields, columns and buttons on each screen, including the pop-up editors and dialogs reached from them. It also covers the screens that are not in the sidebar: first-run set-up, sign-in, change password, the top bar, the search box (Ctrl+K) and the keyboard shortcuts. Amounts are in rupees (₹) and tax is GST. Rules are taken from what the application actually checks; where the code does not say, this document says so.

Last updated: 2026-10-17 (Phase 21: navy grouped sidebar, cashier workspace, Orders page, dashboard widgets, Menu tabs)

How to read it:

- **Required** means the Save / OK button will refuse an empty value (the screen or the server shows a message).
- **Manager approval** means a pop-up asks a manager for their username and password (the cashier does not need a manager to be present if a manager is the one signed in).
- **Source** lines name the screen file under `src/HotelPOS.Desktop/` for developers.
- The sidebar only shows what the signed-in person may open. By default that follows the role (see the Appendix); once an administrator edits permissions under Roles & Permissions, the sidebar follows the permissions instead.

---

## 1. Menu map

Since Phase 21 the sidebar is the navy panel of the owner's mock-ups: a group label, then the screens of the group; the
open screen is the green block. The groups come in this order (plan § 4 D7). A group is only shown when the person can
open at least one screen in it. The sidebar collapses to an icon rail on its own when the window is narrower than 1440 px
(the hamburger button expands or collapses it by hand; the choice is remembered on this terminal).

| Group | Screen | Who sees it (default roles) | Purpose |
|---|---|---|---|
| Overview | Dashboard | Admin, Manager, Waiter | KPI cards, live order status, active orders, table status, recent activity (3.1) |
| Overview | Cashier home | Cashier | Pending bills, own shift, expected cash, print failures, business day (3.1.1) |
| Live Operations | Tables | Admin, Manager, Waiter, Cashier | Live table map; occupy tables, take orders |
| Live Operations | Sections & Tables | Admin, Manager | Set up sections and tables |
| Live Operations | My Orders | Admin, Manager, Waiter | Your active orders; ready ones highlighted |
| Live Operations | Orders | Admin, Manager | Every active order: search, status chips, sort, open the table (3.4, new in Phase 21) |
| Live Operations | Kitchen Display | Admin, Manager, Kitchen | New / preparing / ready tickets |
| Live Operations | Completed Orders | Admin, Manager, Kitchen | Tickets completed today with timings |
| Live Operations | Billing | Admin, Manager, Cashier | The cashier workspace: queue, bill and payment side by side (5.1) |
| Live Operations | Closed Bills | Admin, Manager, Cashier | Settled and voided bills, reprint, refund |
| Live Operations | Print Log | Admin, Manager, Cashier | Prints that failed on this terminal, with a Failed filter and Retry |
| Menu Management | Menu | Admin, Manager | Tabs Categories, Items, Modifiers, Taxes*, Stations*, Preview (6.6) |
| Finance | Payments & discounts | Admin, Manager | Predefined discounts, payment methods, cash denominations (6.7) |
| Finance | Cash shifts | Admin, Manager | All cash shifts of a day; X/Z reports, force close |
| Finance | Day End | Admin, Manager | Close the business day |
| Finance | Day End History | Admin, Manager | Closed days; reopen a day |
| Finance | Period Close | Admin | Lock a GST month or financial year |
| Finance | Tax Returns | Admin, Manager | GSTR-1, GSTR-3B, HSN/SAC, validation |
| Analytics & Reports | Reports | Admin, Manager | Sales, items, payments, taxes, staff, kitchen (one screen; the report type is chosen inside it) |
| People | Users | Admin | Staff accounts |
| People | Roles & Permissions | Admin | Permissions per role |
| Devices | Devices | Admin | Registered terminals |
| Devices | Printers | Admin, Manager | Printers attached to this PC |
| Devices | Kitchen stations | Admin | Opens the Stations tab of Menu |
| Settings | Restaurant Settings | Admin | Shared settings and print templates |
| System | Licence | Admin | Licence status and key |
| System | Backup | Admin | Database backups |
| System | Server | Admin | Notices to terminals; server log |
| System | This Terminal | Admin, Manager | Server address and identity of this PC |
| System | Import | Admin, Manager | Bring menu or customers in from Excel/CSV |
| System | Audit Log | Admin, Manager | Who changed what, when, from which terminal |
| (not in the sidebar) | Customers | Admin, Manager, Cashier | Customer list used by the bill (5.3). Hidden from the menu in Phase 21 (D3); reached from the command palette or the bill's customer panel |

\* Administrators only. That is 29 screens in the sidebar plus Customers. Where a screen has tabs they are listed in its
section below (Sections & Tables, Menu, Payments & discounts, Restaurant Settings, Tax Returns). Phase 22 added Expenses
(Finance group), the Hourly sales and Profit & Loss tabs under Reports and the "Expenses today" dashboard card
(`docs/phases/phase-22-excluded-features.md` F1–F4). Low stock and the floor-plan drawing of the mock-ups still have no
data behind them and are not screens.

After sign-in each person lands on a home screen: Admin and Manager on Dashboard, Kitchen on Kitchen Display, Cashier on the cashier home (shown as "Dashboard"), Waiter on Tables (in the browser and the Android app, on the waiter Home page at /home).

---

## 2. Screens outside the menu

### 2.1 First-run configuration ("Connect this terminal")

Shown automatically the first time the app starts on a PC (no server address saved). The same screen is later available as **This Terminal** (Administration) and is then titled "This terminal".

| Field | Type | Required | Rules / notes |
|---|---|---|---|
| Server address | text | Yes | A web address, for example `http://192.168.1.100:5000`. If `http://` is left out it is added. Must be http or https with a host, and no `?query` or `#fragment`. |
| Terminal name | text | Yes | 2 to 50 characters; letters, digits, `-` and `_` only (for example WAITER-01, KITCHEN-01). Each terminal needs a unique name. |
| Terminal type | dropdown | Yes | Waiter, Kitchen, Billing, Admin. Decides which large-touch layout is used and whether the cash-shift bar appears (Billing terminals only). |
| Display density | dropdown | No | Automatic (from terminal type), Comfort (large touch targets), Compact (mouse and keyboard). |

Buttons: **Test connection** (checks the server and shows the result), **Save**, **Cancel** (not offered on first run). The path of the configuration file is shown at the bottom.

Source: `Modules/Config/ConfigurationView.xaml`

### 2.2 Sign in

| Field | Type | Required | Rules / notes |
|---|---|---|---|
| Username | text | Yes | |
| Password | password | Yes | |

Also shown: restaurant name, a server status line (Server online / Server unreachable / Checking server), the server address and this terminal's description.
Buttons: **Sign in**, **Retry** (re-check the server), **Server settings** (opens the first-run screen).
Messages you may see: server cannot be reached; account locked after too many failed attempts (default 5 attempts, 15 minutes, set in Restaurant Settings); terminal deactivated by an administrator; too many attempts, wait a minute.

Source: `Modules/Auth/LoginView.xaml`

### 2.3 Update required

Shown instead of the sign-in screen when the server needs a newer version of HotelPOS than this terminal runs. Displays installed version, version needed by the server and server version. Buttons: **UPDATE NOW** (downloads from the server and restarts, when the server has the installer), **CHECK AGAIN**, **Server settings**.

Source: `Modules/Auth/UpdateRequiredView.xaml`

### 2.4 Change password (forced and voluntary)

Forced after sign-in when an administrator created or reset the account with "must change password". Voluntary from the sidebar key icon, the Ctrl+K search ("Change my password"), or the user area.

| Field | Type | Required | Rules / notes |
|---|---|---|---|
| Current password | password | Yes | |
| New password | password | Yes | Must meet the password rule set in Restaurant Settings (default: at least 8 characters, letters and digits; the rule is shown under the field). Must differ from the current password. |
| Confirm new password | password | Yes | Must match the new password. |

Buttons: **Save password**, and **Cancel** (on a forced change this button reads **Sign out**; the person cannot continue without changing the password).

Source: `Modules/Auth/ChangePasswordView.xaml`

### 2.5 Top bar and status bar

| Item | What it does |
|---|---|
| Menu button (hamburger) | Collapse or expand the sidebar; remembered on this terminal. Below 1440 px window width the sidebar collapses to the icon rail by itself (Phase 21) so the cashier's three panes fit at 1366 px. |
| Restaurant name and logo block | Shows the restaurant name from settings. |
| User block (initials, name, roles) with bell, key and sign-out icons | **Notifications…** (bell icon, every role: opens a dialog with the same Pop-up and Sound list as Restaurant Settings, saved at once for this terminal; Esc or **Done** closes it); Change my password; Sign out. |
| Page title and description | Name and one-line purpose of the open screen. |
| Search box "Search or jump to... Ctrl K" | Opens the command palette (2.6). |
| Clock | Current time. |
| Light/dark theme button | Switches theme; remembered on this terminal. |
| Notices bell with unread count | Opens the Notices panel: messages sent by an administrator from Administration > Server, since you signed in (last 50 kept). Opening it marks them read. |
| Keyboard shortcuts button (?) | Shows the shortcut list (same as F1). |
| Licence banner | Appears for a trial, a licence about to expire, or an expired licence (read-only). **Licence** button opens the Licence screen. |
| Update banner | Appears when the server has a newer version; **Update now** installs it after a confirmation. |
| Status bar: connection indicator | Green Connected, amber Connecting / Reconnecting, red Disconnected; with the server address, terminal name and app version. |
| Status bar: failed prints | Appears when a print failed; **Retry** or **Discard**. |
| Cash shift bar (billing terminals only) | See 2.7. |

Source: `Shell/ShellView.xaml`

### 2.6 Command palette (Ctrl+K)

A search box over the whole window. Type a screen name to jump to it, a table code (T5, T05, "table 5") to open that table, an order number (#12, "order 12") or a bill number or invoice number (4 or more characters of an invoice number also match). Results are grouped Go to, Actions, Found. Use Up/Down to move, Enter to open, Esc to close. Only screens the person may open are listed.
Actions offered: New order (if Tables is allowed), Back up now (if Backup is allowed), Switch light/dark theme, Change my password, Keyboard shortcuts, Sign out.

Source: `Shell/CommandPaletteViewModel.cs`, `Shell/ShellView.xaml`

### 2.7 Cash shift bar (billing terminals)

A strip under the top bar, shown only on a terminal of type Billing for a user who may operate a cash drawer. It shows "no shift open" or the shift status and expected cash.
Buttons: **Open shift** (when none), **X report**, **Pay in / out**, **Hand over**, **Close shift**. Each opens a panel; the count panel and the pay in/out panel are described under Billing > Cash Shifts (4.4). If a shift has to be closed by someone else at day end, a notice with **Dismiss** is shown.

Source: `Modules/Cash/ShiftBarView.xaml`

### 2.8 Keyboard shortcuts

| Keys | Action |
|---|---|
| Ctrl+K | Search / jump to (command palette) |
| Ctrl+D | Dashboard |
| Ctrl+T | Tables |
| Ctrl+B | Billing |
| Ctrl+R | Reports |
| F1 | Show the shortcut list |
| F2, F3 … / F9 | On a bill: F2 is the cash payment method, F3 onwards the other active methods in their sort order (Payments & discounts), F9 split payment (Phase 21, D6) |
| Esc | Close the open panel or palette; on a bill, back to the queue |
| F11 | Full screen |

Ctrl+D/T/B/R only work if the person may open that screen.

---

## 3. Service

### 3.1 Dashboard

Purpose: today's figures at a glance, refreshed live. Opened by Admin, Manager, Waiter (a Cashier has their own home, 3.1.1). Managers and administrators see the restaurant figures; other roles see their own figures (the code uses the "own figures" view for every non-manager role; it was not checked whether a cashier's figures are meaningful).

Phase 21 (plan § 5 A2–A6) lays the manager view out like the owner's admin mock-up. Header: "Business day <date>", "Welcome back, <name> · Updated <time>", an **Online** pill (the realtime connection) and the refresh icon.
KPI cards (manager view, one row): **Today's sales** (settled bills; red/green delta vs yesterday from the 7-day series), **Orders today** (and "n open now"), **Average order value** (sales ÷ bills settled, derived on the client) and **Occupied tables** (x / total with an occupancy bar). No sparklines and no Low stock card (no inventory data).
**Live Order Status**: a donut of the active orders by status (from `api/orders/active`), with a legend of status badge, count and percent.
**Active Orders**: every order not yet paid, search (order, table or waiter), status chips with counts, sort (Newest first / Oldest first / Highest amount), ten rows per page (#, TABLE, WAITER, ITEMS, TOTAL, STATUS, ELAPSED, **View** opens the table), **View all** opens the Orders screen (3.4).
**Table status**: one card per table (code, status badge, guests and order number when occupied, seats) in status colour, **View floor plan** opens Tables.
**Recent activity**: the last eight audit-log rows as sentences ("cashier1 settled bill INV-…"), with live notices prepended as they arrive; **View all** opens the Audit Log. Without the audit permission the widget falls back to the bell feed (D10).
Then, as before: "Net sales - last 7 days" bars, "Payments today" donut by method with ₹ and percent, "Top items today" (picture or initials, name, quantity, amount), **Kitchen performance today**, **Staff today** and **Terminals** side by side (Terminals lists devices for administrators only, because `api/admin/devices` is administrator-only; a manager sees a placeholder). A panel whose endpoint fails stays empty; the rest of the dashboard is unaffected.
Tiles (own view, waiter): My orders today (and still open), My sales today (settled bills), Tables I am serving (and guests today).
Phase 22 added a fifth KPI card, **Expenses today** (live expenses dated the business day; detail "… after expenses"; opens Reports → Profit & Loss), and the **Hourly sales · today** bars (F1). The **Staff today** column now lists live presence (P4): name, role chip, online with device names / open shift / active orders, "Nobody connected" when empty, captioned "Live connections, not attendance" (hub presence is in-memory: empty after an API restart, can flap on phones); it comes from `DashboardDto.StaffOnDuty` (`GET api/staff/on-duty`). Not on the dashboard: low stock.

Source: `Modules/Dashboard/DashboardView.xaml`

#### 3.1.1 Cashier home (Phase 20.6)

Purpose: the Cashier role's Dashboard (menu entry "Dashboard", module key `cashier-home`). Opened by Cashier. Refreshed live (bill, payment and shift events, one reload per burst).
Tiles: **Pending bills** (count and "oldest waiting n min"; opens Billing), **My shift** (takings since the shift opened, from the X report, and the float; "No shift" when none is open on this terminal), **Expected cash** (from the X report; "Blind count" when the drawer count is blind; pay-ins, pay-outs and cash sales in the detail), **Business day** (Open, then Closed or Reopened as the day-end events arrive; the Day End precheck is manager-only, so a cashier's tile follows the events).
List: **Waiting for payment**, up to six bills oldest first (table, order and waiter, minutes waiting, total).
**Print failures** (Phase 20.8, between Expected cash and Business day): the number of prints that failed on this terminal, from the print queue; it opens the Print Log on its Failed filter.
Buttons: refresh icon.

Source: `Modules/Cash/CashierHomeView.xaml`

#### 3.1.2 Waiter home, browser and Android (Phase 20.6)

Page `/home` (the landing after sign-in for users who may view tables, other than Kitchen). Sections with counts, each a list of touch rows (min 44 px) that open the table: **My tables** (tables where the signed-in waiter has an open order, with status), **Ready** (their orders that are ready to serve), **Bill requests** (their orders with a bill requested or issued). Data: `api/orders/active` filtered to the waiter; refreshed on the order, table, bill and payment events with a 250 ms debounce, and after a reconnect. The Tables page stays at `/tables` (nav link "Tables"); a "Home" nav link is not added here (the layout belongs to step 20.1).
Kitchen Display (browser) header now shows **Delayed** (open tickets past the late minutes) and **Completed today** (from the completed-tickets list).

Source: `Client.Shared/Pages/Waiter/HomePage.razor`

### 3.2 Tables

Purpose: the live table map. Opened by Admin, Manager, Waiter, Cashier.

Controls: section filter tabs (All plus each section), summary text, refresh. Each table tile shows code, time since occupied, status line and guests. Legend: Available, Occupied, Ordering, Preparing, Ready, Billing, Out of service. Click a table to open its details panel.

**Waiter Tables in the browser and Android app (Phase 22 P3).** The toolbar has a **List / Map** toggle, kept per device (`hotelpos.tables.view-mode`); until it is used, phones narrower than 600 px start on List and wider screens on Map. **Map** draws each section's placed tables as tiles at the position, size, shape (Round or Rectangle) and rotation set in Sections & Tables, scaled to the screen width (never below half size: the plan then scrolls sideways; tiles keep a 44 px tap target and show only the code when narrow). Tiles use the same status colours as the cards, show the reservation badge, and open the table on tap. Tables without a position are listed under **Not on the map** as normal cards. A section with no placed table shows the list with "No floor plan for this section yet". Status updates live; a saved layout change reloads the plan. **By section / Available first** applies to List only.

#### 3.2.1 Table details panel

Shows table code, status, name, guests, occupied since, and the current order with its batches and lines (item, options, total) and the bill status.

| Field | Type | Required | Rules / notes |
|---|---|---|---|
| Number of guests | on-screen number pad | Yes (to occupy) | 1 to 99 (two digits). Keys 0-9, C (clear), backspace. |

Buttons (each shown only when it applies):
- **OCCUPY** - seat guests on an available table.
- **NEW ORDER** / **RESUME ORDER** (when an unsent draft is saved on this terminal) - opens the order builder (3.2.2). Needs the right to take orders.
- **OPEN ORDER** - open a draft order.
- **ADD ITEMS** - add items to a sent order.
- **SERVED** - mark a ready order as served (needs the right to take orders).
- **REQUEST BILL** - send the order to billing (waiter, cashier or manager).
- **RELEASE** - free an occupied table that has no order (asks to confirm).
- **CANCEL ORDER** - asks for a reason ("required once the kitchen has started").
- Cancel-item icon on a sent line - asks for a reason for the kitchen. Allowed while the kitchen ticket is new or accepted; once preparing only a manager may cancel.
- Close (X).

Source: `Modules/Tables/TableMapView.xaml`, `Modules/Tables/TableDetailsView.xaml`

#### 3.2.2 Order builder

Reached from Tables > New order / Add items. Left: category tabs (with "★ Favourites" first once there is anything in it) and a grid of item tiles (name, price, "+ options", SOLD OUT, a star to pin). Right: the order cart. The same builder is the waiter phone page in the browser/Android app (stars are on each item card).

| Field | Type | Required | Rules / notes |
|---|---|---|---|
| Guests | - / + stepper | Yes | 1 to 99. |
| Quantity per line | - / + stepper | Yes | 1 to 99. |
| Note for the kitchen (per line) | text + quick buttons | No | Quick notes: Less spicy, Extra spicy, No onion, No garlic, Less oil, Jain, Parcel. Up to 500 characters. |
| Search items | text, as you type | No | Matches part of an item name or code, any case, across all categories (the category tabs are ignored while the box has text). Clear it to go back to the tab. |
| Pin star (on each item tile) | toggle | No | Pins or unpins the item for the "★ Favourites" tab on this terminal / this browser only. Not shared with other devices or users, no server change. |

Buttons: **Back to the tables**, **SAVE DRAFT** (kept on this terminal), **SEND TO KITCHEN** (or **SEND ADDED ITEMS** when adding), remove-line icon, note icon.
Connection banner appears if the terminal is offline; unsent items are kept on this PC.

**★ Favourites** (first tab, shown only when it has items, and then selected on opening): pinned items in pin order, followed by the 8 items most often sent from this terminal. A send counts each item once; the counter keeps the 200 most-ordered items. Stored in the terminal profile (`preferences.json`) on the desktop and in browser storage on the web/Android app.

##### Item options pop-up
Opens when an item has modifier groups. Each group shows its rule ("Choose 1", "Optional - up to 3", "Choose 1 to 2") and its options with price change. Quantity stepper. Buttons: **Cancel**, **Add to order**. Add refuses until every group's rule is met.

Source: `Modules/Orders/OrderBuilderView.xaml`

### 3.3 My Orders

Purpose: the signed-in waiter's active orders; ready orders are highlighted green. Opened by Admin, Manager, Waiter.
Each card shows order number and table, time elapsed, status, items / guests / waiter, and approximate total. Summary text "n active orders"; refresh. Click a card to open the table.

Source: `Modules/Orders/OrderListView.xaml`

### 3.4 Orders (Phase 21)

Purpose: every active order in the restaurant on one page (the dashboard's Active Orders widget in full). Opened by Admin, Manager (Live Operations group).
Controls: search (order number, table or waiter), "n active orders", sort (Newest first / Oldest first / Highest amount), refresh; status chips with counts (**All**, then one per status present: Sent to kitchen, Preparing, Ready, Served, Bill requested …). The dashboard's **View all** opens this page with the chip it was on (deep link).
Columns: #, TABLE, WAITER, ITEMS, TOTAL, STATUS (badge with icon and word), CREATED (time), ELAPSED ("20 h 39 min"), **View** (opens the table; double-click does the same). Refreshed live on order, table and bill events. Empty state "No active orders".

Source: `Modules/Orders/ActiveOrdersView.xaml`, `Modules/Orders/ActiveOrderQueries.cs`

---

## 4. Kitchen

### 4.1 Kitchen Display

Purpose: large touch tickets for the kitchen. Opened by Admin, Manager, Kitchen.
Controls: station filter (All stations or one station), **Sold out...**, refresh. Three columns with counts: NEW, PREPARING, READY. A ticket shows table, "+ADD" tag for added items, elapsed time, ticket number, waiter, status, lines with options and notes (cancelled lines struck through). Tickets turn amber after the "warn" minutes and red after the "late" minutes set in Restaurant Settings (default 10 and 20).
Ticket buttons by status: **ACCEPT** (new), **START** (new or accepted), **READY** (preparing), **DONE** and **RECALL** (ready).
Item pictures (Phase 22): each ticket line carries the menu item's picture URL (`ImageUrl`, null when the item has no picture). The checkbox **Show item pictures** beside **Sold out...** puts a small thumbnail before each line; it is a per-terminal setting, **off by default**, kept with the station choice. In the browser, **Kitchen > Station** has the same device setting, **Show item pictures on tickets** (off by default); the ticket detail pane always shows a larger thumbnail. A picture that fails to load is hidden and the line stays text-only.

#### 4.1.1 Sold-out panel ("Item availability")
List of menu items with a toggle button per item (mark sold out / available again). Button **Close**.

Source: `Modules/Kitchen/KitchenDisplayView.xaml`

### 4.2 Completed Orders

Purpose: tickets completed today. Columns: TICKET, TABLE, ITEMS, IN (time received), WAIT, PREP, TOTAL, STATUS, and a **Reprint** button per row. Summary text and refresh.

Source: `Modules/Kitchen/CompletedOrdersView.xaml`

---

## 5. Billing

### 5.1 Billing (cashier workspace, Phase 21)

Purpose: the cashier's one screen for taking payments, laid out like the owner's cashier mock-up: three panes side by side. Opened by Admin, Manager, Cashier. The cash shift bar (2.7) stays above it because cash payments need an open shift.

**Left pane — Bills** (400 px): header "n bills waiting" and refresh; search (order, table or bill number); chips **All**, **Waiting** (count), **Invoiced**, **Partly paid**, **Paid** (today's settled bills, read-only; reprints and refunds stay under Closed Bills); sort **Oldest first** / **Highest amount**. One card per bill: table code, order number, status badge (Waiting / Invoiced / Partly paid / Paid, icon + word), waiter, minutes waited, total; the selected card has a green border. Clicking a card claims the bill (a bill is held by one cashier at a time; a claim lasts a few minutes; a bill held elsewhere opens read-only and shows who has it). Empty states: "No bills waiting" / "No matching bills" / "No bills paid today".

**Centre pane — the order** (the former bill screen, 5.1.1): "Table T02 · Order #1015", invoice number or "Not invoiced yet", guests, waiter, requested time; the lines with picture or initials, quantity × name, options and per-line note ("Less oil" badge), unit price and amount; an order-notes banner when the order has notes; Subtotal, Discount with **Add discount** / the applied "Discount 5% · reason" and amount, the real tax rows (CGST/SGST or IGST), Round off, Total, payments made and Balance due; the customer line with **Customer...**; actions **Finalize & print** (open bill) or **Show invoice** (finalized), **Reopen**, **Void**; **Take over** (managers) when another cashier holds the bill; × / Esc clears the selection.

**Right pane — Payment** (340 px, always visible): status pill (Waiting / Paid), "Amount due" hero, one button per active payment method from Payments & discounts in sort order with its hotkey (**F2** = the cash method, **F3**… the rest; a method that is inactive is not shown; without an active cash method the cash controls are hidden), **Split payment F9**, Amount to pay, quick amounts (Exact, then round-ups), Amount received and Change (cash only), Reference (methods that ask for one), **Complete payment**, and a hint line "F2 cash · F3–F8 other methods · F9 split · Esc back to the queue". After payment the pane shows the inline **Paid** state: invoice number, change to give back, the tenders, **Print receipt**, **Print invoice**, **Next bill** (clears the selection).

Source: `Modules/Billing/CashierWorkspaceView.xaml`, `Modules/Billing/CashierWorkspaceViewModel.cs` (the queue), hosting `BillDetailView.xaml` / `BillDetailViewModel.cs` (bill, payment, idempotency and permission logic unchanged)

#### 5.1.1 Bill (centre and right panes)

Opened by selecting a card, or from the search box with a bill number (deep link into the workspace). Rules for the action buttons:

Rules for the action buttons:
- Editing and paying need the bill to be held by you.
- **REOPEN** is offered while nothing has been paid; it asks for a reason; if an invoice number was already issued it also needs manager approval; the order goes back to the waiter.
- **VOID** is offered for a finalized bill with nothing paid; asks for a reason and needs manager approval; the order is cancelled and the table released.

##### Discount panel
| Field | Type | Required | Rules / notes |
|---|---|---|---|
| Predefined discount | list (click one) | - | Defined under Discounts & Payments. Large ones need manager approval. |
| Manual discount type | choice | Yes | Percent % or Amount. |
| Manual value | number | Yes | Percent: more than 0 and up to 100. Amount: more than 0, at most 2 decimals, at most ₹1,00,00,000. |
| Reason | text | Yes (manual) | Up to 300 characters. |

Buttons: **APPLY**, **REMOVE DISCOUNT**. A cashier may give a discount up to the "max cashier discount percent" setting (default 10) without approval; above that a manager approval pop-up appears.

##### Customer panel (with customer picker)
Printed on the invoice. A GSTIN is needed for a business (B2B) invoice.

| Field | Type | Required | Rules / notes |
|---|---|---|---|
| Find a customer | search box | No | Searches name, phone or GSTIN; the picker shows up to 8 matches (name, phone, GSTIN). Click one to fill the fields below. "No customer matches" is shown when empty. |
| Name | text | No | Up to 100 characters. |
| Phone | text | No | Digits, spaces, + and - only; up to 20 characters. (No fixed 10-digit rule in the code.) |
| GSTIN | text | No | If entered: exactly 15 characters, the standard GSTIN pattern, a known GST state code and a correct check character. A hint under the field says what is wrong, or "Valid. State ...". A valid GSTIN fills the customer state. |
| Legal name (as registered) | text | No | Up to 200 characters. |
| Customer state | dropdown | No | GST states. |
| Place of supply | dropdown | No | Restaurant's own state unless the service is supplied in another state (then IGST applies). |

Buttons: **SAVE**, **CLEAR CUSTOMER** (takes the customer off the bill; shown when the bill has one). If the typed details still match the picked customer, the bill stays linked to that customer record.

##### Payment pane
Always open on the right (Phase 21). A method button (F2 = cash, F3… the others) or **Split payment** (F9) sets the mode.

| Field | Type | Required | Rules / notes |
|---|---|---|---|
| Amount | ₹ amount | Yes | More than 0, at most 2 decimals. Non-cash cannot exceed the balance. |
| Cash received (cash only) | ₹ amount | No | Must not be less than the amount. Quick buttons: Exact, 50, 100, 500, 2000. Shows Change. |
| Reference / approval code | text | Yes for methods that ask for a reference (card, UPI by default) | Up to 100 characters. |
| Split lines (Split only) | list: method, amount, reference | Yes | Add a line per method with the **Add:** buttons; each line needs an amount; lines must add up exactly to the balance (messages: "... is still unpaid", "lines are ... more than the balance"). |

Buttons: **Complete payment**. After payment the pane turns into the **Paid** state (invoice number, change to give back, the tenders) with **Print receipt**, **Print invoice**, **Next bill**.

##### Invoice preview
**PRINT PREVIEW** and **PRINT** show the invoice as a TAX INVOICE: restaurant name, address, GSTIN, items, subtotal, discount, tax rows, round off, total, footer.

##### Manager approval pop-up
Appears whenever an action needs a manager (discount above the limit, void, reopen of an invoiced bill, refund, cash difference, pay-out above the limit, reopening a shift or day).

| Field | Type | Required | Notes |
|---|---|---|---|
| Manager username | text | Yes | |
| Manager password | password | Yes | Checked once; the approval is good for one action for about 2 minutes. A locked manager account or too many tries shows a message. |

Source: `Modules/Billing/BillDetailView.xaml`, `Modules/Billing/PaymentViewModel.cs`, `Modules/Billing/Approvals.cs`

### 5.2 Closed Bills

Purpose: settled and voided bills, reprint, refund. Opened by Admin, Manager, Cashier.

| Field | Type | Required | Rules / notes |
|---|---|---|---|
| Business day | date | No | Empty = today. **Today** button resets. |
| Search | text | No | Table, invoice or order number. |

Grid columns: INVOICE, TABLE, ORDER, TIME, TOTAL, STATUS. Selecting a bill shows its lines, totals, print history and PAYMENTS (method, detail, amount).
Buttons: **Reprint** (per payment: reprints the receipt), **Refund...** (per payment), **REPRINT INVOICE** (asks for a reason, kept in the audit log), **PREVIEW**, **SAVE PDF**.

##### Refund steps
1. Amount to refund: up to the amount still refundable for that payment (blank means all); at most 2 decimals.
2. Reason ("Why is the money returned?") - required.
3. Manager approval pop-up if the refunder is not a manager.

Source: `Modules/Billing/ClosedBillsView.xaml`

### 5.2.1 Print Log (Phase 20.8)

Purpose: find prints that did not come out on this terminal and print them again. Opened by Admin, Manager, Cashier (Billing operate permission). Also opened from the cashier home **Print failures** tile and from a "did not print" notice in the bell, both with the **Failed** filter already chosen.

| Field | Type | Required | Rules / notes |
|---|---|---|---|
| Show | list | No | **All copies** or **Failed**. |

Grid columns: WHEN, DOCUMENT, COPY (Original, or Copy n with DUPLICATE), STATUS (Printed, Failed, Failed (retried), Waiting), PRINTER, WHY (the printer error), and a **Retry** button.
Retry prints the document again through the normal print path: the next copy number is reserved on the server, so the paper says DUPLICATE, the failed copy keeps its number in the log, and the result of the new copy is reported back. The outcome appears above the grid ("printed" or "failed again: reason") and the list refreshes. Retry is available only on the latest copy of a document and only for printer output, not PDFs. If this terminal has no printer for the document type, a notice says so and nothing changes.
What is listed: the server has no list of print copies across documents, so the screen starts from the failed prints this terminal still holds (kept for 8 hours or until the app closes) and shows every copy of those documents that this terminal printed. A failure on another terminal is not listed here; that terminal retries its own.
Buttons: refresh icon.

Source: `Modules/Printing/PrintLogView.xaml`

### 5.3 Customers

Purpose: search, add, edit and deactivate customers; the bill's picker draws from this list. Opened by Admin, Manager, Cashier.
Phase 21 (D3): the screen is no longer in the sidebar (module key `customers`, `ShowInNav = false`); it still opens from the command palette ("Customers") and from Find. The bill's customer panel keeps the picker, says "add the customer under Manage customers" when nothing matches, and has a **Manage customers…** link (`BillDetailViewModel.ManageCustomersCommand`) that opens the screen.
Controls: search (name, phone or GSTIN), Show inactive, refresh, **New customer**, paging (25 per page).
Columns: NAME, PHONE, GSTIN, LEGAL NAME, STATUS.
Buttons: **Edit**, **Deactivate / Activate** (deactivate asks to confirm), previous/next page.

#### 5.3.1 Customer editor

| Field | Type | Required | Rules / notes |
|---|---|---|---|
| Name | text | Yes | Up to 100 characters. |
| Phone | text | No | Digits, spaces, + and - only; up to 20. Used to match existing customers on import. |
| Email | text | No | Must be a valid email; up to 200. |
| GSTIN | text | No | Same 15-character GSTIN check as on the bill, with a live hint. |
| Legal name (as registered) | text | No | Up to 200. |
| State | dropdown | No | GST states. |
| Address | text | No | Up to 500. |

Buttons: **Cancel**, **Save** (Create or Save).

Source: `Modules/Customers/CustomersView.xaml`

### 5.4 Cash Shifts

Purpose: every cash shift of a day with float, expected, counted and variance; X/Z reports; force close. Opened by Admin, Manager (permission "view and close any shift").
Controls: day navigation (previous, **Today**, next).
Columns: TERMINAL, CASHIER, OPENED, CLOSED, FLOAT, EXPECTED, COUNTED, VARIANCE, STATUS.
Buttons: **X / Z report** (opens the report panel), **Reopen...** (asks for a reason; manager approval; the shift is counted again when it closes), **Force close...** (open shifts only; opens the count panel).

#### 5.4.1 Shift count panel (open, close, hand over, force close)
Used by the cash shift bar and by Force close.

| Field | Type | Required | Rules / notes |
|---|---|---|---|
| Denomination counts | grid of notes and coins with - / + steppers and quantity | Yes | One quantity per note or coin (set under Discounts & Payments > Denominations). Shows COUNTED, EXPECTED, DIFFERENCE. **Clear** resets. With "blind count" on, the expected cash is hidden until the count is entered. |
| Next cashier | dropdown | Yes (hand over only) | The cashier who takes over. |
| Reason for the difference / force close | text | Yes when counted cash differs from expected (close, hand over) and always for force close | Up to 300 characters. |

Buttons: **Cancel**, and the action button: **OPEN SHIFT**, **CLOSE SHIFT**, **HAND OVER**, **FORCE CLOSE**.
A difference above the "cash variance approval limit" (default ₹100) needs manager approval.

#### 5.4.2 Pay in / pay out panel

| Field | Type | Required | Rules / notes |
|---|---|---|---|
| Type | choice | Yes | Pay in (cash put into the drawer), Pay out (supplier, vegetables), Till voucher (small expense). |
| Amount | ₹ amount | Yes | More than 0, at most 2 decimals. |
| Reason | text | Yes | Up to 300 characters. |

Buttons: **Cancel**, **RECORD**. Pay-outs and vouchers above the "pay-out approval limit" (default ₹1,000) need manager approval.

#### 5.4.3 Shift report panel (X / Z report)
Shows the CASH DRAWER summary (float, cash sales, pay-ins, pay-outs, expected, counted, variance and the variance reason), PAYMENTS ON THIS TERMINAL and CASH MOVEMENTS. Buttons: **Close**, **Preview**, **PRINT**.

Source: `Modules/Cash/ShiftsView.xaml`, `Modules/Cash/ShiftCountPanelView.xaml`, `Modules/Cash/CashMovementPanelView.xaml`, `Modules/Cash/ShiftReportPanelView.xaml`, `Modules/Cash/DenominationSheetView.xaml`

### 5.5 Day End

Purpose: close the business day in four steps: checks, totals, run (verified backup, lock, report pack), done. Opened by Admin, Manager (permission "run the day-end close").

**Step 1 Checks.** Blocking items found by the server, each with an **Open** button to the screen where it can be settled: open orders (Tables), unpaid bills (Billing), open shifts (Cash Shifts), open kitchen tickets (Kitchen Display). An item can instead be overridden:

| Field | Type | Required | Rules / notes |
|---|---|---|---|
| Reason for the override | text | Yes, per overridden item | Up to 300 characters. Overrides are saved with the close and audited. |

Buttons: **Check again**, **Override...**, **CONTINUE** (enabled when every item is settled or overridden). If the day is already closed a notice offers **Day End history**.

**Step 2 Totals.** Shows Sales, Payments by method, Shifts and variances, and:

| Field | Type | Required | Rules / notes |
|---|---|---|---|
| Non-till collections: method | text | Yes to add a row | Up to 40 characters (for example owner's UPI, cheque). |
| Amount | ₹ amount | Yes | More than 0, at most 2 decimals. |
| Reference | text | No | Up to 100 characters. |

Buttons: **Add**, **Back**, **RUN DAY END** (takes a verified backup, locks the day, saves the report pack).
**Step 4 Done.** Shows the backup file, the report-pack folder and its files, with **Day End history**. If the server does not answer, the screen tells you to check Day End history before running again.

Source: `Modules/Cash/DayEndView.xaml`

### 5.6 Day End History

Purpose: closed business days with totals, backup and report pack; reopen a day. Opened by Admin, Manager.
Columns: BUSINESS DAY, STATUS (Closed or Reopened), CLOSED (time and who), BILLS, NET SALES, VARIANCE. The selected day shows its detail lines and any OVERRIDES.
Button: **Reopen day...** - asks for a reason (required, up to 300 characters) and a manager approval; bills and payments of that day become editable again.

Source: `Modules/Cash/DayEndHistoryView.xaml`

---

## 6. Management

### 6.1 Reports

Purpose: ten reports with export. Opened by Admin, Manager (permission "view reports").

| Field | Type | Required | Rules / notes |
|---|---|---|---|
| Report | list on the left | Yes | See the list below. |
| Date range | dropdown | Yes | Today, Yesterday, This week, This month, Last month, Custom. |
| From / To | dates | Yes for Custom | Business days (a day starts at the "business day start time" setting, default 04:00). |
| Year | dropdown | Yes (Monthly sales only) | Replaces the date range. |

Buttons: **Run**, **Export CSV**, **Export Excel**, **Save PDF**. Below the grid: summary line (rows) and totals for numeric columns where a total makes sense. "No rows for this period" when empty.

| Report | Columns shown (as headed on screen) |
|---|---|
| Daily sales | Day, Bills, Covers, Subtotal, Discounts, Tax, Gross sales, Refunds, Net sales |
| Monthly sales | Month, Bills, Covers, Discounts, Tax, Gross sales, Refunds, Net sales |
| Item sales | Item name, Category, Quantity, Amount, Share percent |
| Category sales | Category, Items, Quantity, Amount, Share percent |
| Payments | Method, Payments, Amount, Refunds, Refund amount, Net |
| Cancellations | Order number, Table code, Waiter name, Cancelled at, Cancelled by, Cancel reason, Items, Approx amount |
| Discounts | Invoice number, Table code, Discount, Type, Value, Subtotal, Amount, Approved by, Settled at |
| Tax collection | Tax name and rate percent, Bills, Taxable amount, Tax amount |
| Waiter performance | Waiter name, Orders, Covers, Net sales, Average order value, Cancellations |
| Kitchen performance | Station, Tickets, Average accept minutes, Average prep minutes, Average total minutes, Late tickets, Late percent |

Column headings come straight from the report's field names, so the wording on screen may differ slightly from the table above.

Source: `Modules/Reports/ReportsView.xaml`

### 6.2 Tax Returns (GST hub)

Purpose: GSTR-1, GSTR-3B and the HSN/SAC summary for a month, with the GST portal JSON, Excel and PDF. Opened by Admin, Manager (permission "view GST reports").

| Field | Type | Required | Rules / notes |
|---|---|---|---|
| Month | dropdown | Yes | |
| Year | dropdown | Yes | |
| Section (GSTR-1 tab only) | dropdown | Yes | B2B invoices, B2CL invoices, B2CS summary, Credit notes, HSN summary, Documents issued. |

Tabs: **GSTR-1**, **GSTR-3B**, **HSN summary**, **Validation**.
Buttons: **Show**, **Export JSON** (GSTR-1 tab), **Export Excel**, **Save PDF**.
A red banner appears when the month has errors that must be fixed before filing or closing it ("n error(s) ... See the Validation tab"). The summary line shows invoices, invoice value, taxable value, IGST, CGST, SGST and credit-note tax.

Columns by section:
- B2B and B2CL: Invoice number, Invoice date, Customer GSTIN, Customer name, Place of supply, Invoice value, Taxable value, IGST, CGST, SGST. (B2CL = inter-state invoices to unregistered buyers above the B2CL threshold setting, default ₹1,00,000.)
- B2CS summary: Supply type, Place of supply, Rate, Taxable value, IGST, CGST, SGST.
- Credit notes: Section, Note number, Note date, Invoice number, Customer GSTIN, Place of supply, Note value, Taxable value, IGST, CGST, SGST.
- HSN summary: HSN/SAC, Description, Rate, Quantity, Total value, Taxable value, IGST, CGST, SGST.
- Documents issued: Document, From, To, Total issued, Cancelled, Net issued.
- GSTR-3B: Table, Description, Taxable value, IGST, CGST, SGST, Cess.
- Validation: Severity, Code, Reference, Message.

Source: `Modules/Gst/GstHubView.xaml`

### 6.3 Period Close

Purpose: close a month or a financial year after filing, with a pre-check; reopen with a reason. Opened by Admin only (permission "close and reopen GST periods").

| Field | Type | Required | Rules / notes |
|---|---|---|---|
| Financial year | dropdown | Yes | Last 5 years and the next (April to March). |

Grid columns: PERIOD, STATUS, CLOSED. Selecting a period shows its title, the GST pack text and CHECKS BEFORE CLOSING (each with a pass/fail mark, title and details).
Buttons: **Close period...** (asks to confirm: from then no new, edited, voided or refunded bills or payments dated in that period are accepted; the GST pack is written next to the backups), **Reopen period...** (reason required, recorded in the audit log). Terminals open on the GST screens are told when a period closes or reopens.

Source: `Modules/Gst/PeriodCloseView.xaml`

### 6.4 Import

Purpose: bring the menu or customers in from Excel or CSV. Opened by Admin, Manager (permission "import data"). A four-step wizard.

**Step 1 - choose what to import and the file**

| Field | Type | Required | Rules / notes |
|---|---|---|---|
| What | choice | Yes | Menu or Customers. |
| File | file picker | Yes | `.xlsx` or `.csv`; at most 5 MB and 5,000 rows (split larger files). |

Buttons: **CHOOSE FILE...**, **Undo this import** (appears when a previous import can be undone).

**Step 2 - match the columns.** One dropdown per field, pre-filled by recognising common column names. Fields marked * are required; the app refuses to continue while a required field is not matched.

| Kind | Fields (hint) |
|---|---|
| Menu | Category*, Item name*, Price*, Tax (a rate such as 5 or 5%, or an existing tax name), Preparation station (blank = first active station), Item code, Description, HSN/SAC, Modifiers (example "Size: Small, Large +20; Extras: Cheese +15"). Items already present (same category and name) are updated. |
| Customers | Name*, Phone (matches existing customers), Email, GSTIN, Legal name, State code, Address. Customers are matched by phone, then GSTIN. |

Buttons: **Back**, **CHECK THE ROWS**.

**Step 3 - check the rows.** Summary "n rows: n ready (n new, n to update), n with errors"; error grid columns ROW, COLUMN, PROBLEM (first 200 shown). Checkbox **Skip the rows with errors and import the rest**. Buttons: **IMPORT** (needs at least one valid row, and either no errors or the checkbox ticked), **Back**.

**Step 4 - done.** Result text; **UNDO THIS IMPORT** removes the import completely; **Import another file**. Terminals pick up a new menu by themselves.

Source: `Modules/Licensing/ImportWizardView.xaml`

### 6.5 Sections & Tables

Purpose: create sections and tables. Opened by Admin, Manager (permission "configure sections and tables"). Tabs: **Tables**, **Sections**.

#### 6.5.1 Tables tab
Controls: section filter, Show inactive, refresh, **New table**.
Columns: CODE, NAME, SECTION, SEATS, STATUS, ACTIVE.
Buttons: **Edit**, **Set out of service / Return to service** (setting out of service asks to confirm; the table shows grey and cannot be occupied).

##### Table editor
| Field | Type | Required | Rules / notes |
|---|---|---|---|
| Code (e.g. T05) | text | Yes | 1 to 10 letters, digits or `-`. |
| Name (optional) | text | No | Up to 100 characters. |
| Section | dropdown | Yes | |
| Seats | number | Yes | 1 to 50 (new tables start at 4). |
| Active (shown on the table map) | checkbox | - | |

Buttons: **Cancel**, **Save**.

#### 6.5.2 Sections tab
Controls: refresh, **New section**. Columns: ORDER, NAME, ACTIVE TABLES, STATUS. Button **Edit**.

##### Section editor
| Field | Type | Required | Rules / notes |
|---|---|---|---|
| Name | text | Yes | Up to 100 characters, e.g. Ground Floor. |
| Sort order (position on the table map) | number | Yes | Whole number 0 to 9999. |
| Active | checkbox | - | A section can be deactivated only when all its tables are inactive. |

Buttons: **Cancel**, **Save**.

Source: `Modules/Admin/FloorView.xaml`, `Modules/Admin/TablesView.xaml`, `Modules/Admin/SectionsView.xaml`

### 6.6 Menu

Purpose: categories, items, prices, modifiers, taxes and preparation stations. Opened by Admin, Manager (permission "manage menu"); the only screen of the Menu Management group. Tabs, in this order since Phase 21: **Categories**, **Items**, **Modifiers**, **Taxes** (administrators only), **Stations** (administrators only), **Preview**. The command palette and the Devices › Kitchen stations entry deep-link to a tab by its title (A7). Every change is sent to all terminals automatically.

#### 6.6.1 Items tab
Controls: search (name or code), category filter, Inactive (also show inactive items, in grey), refresh, **New item**.
Columns: CODE, NAME, CATEGORY, PRICE, TAX, STATION, IN STOCK.
Buttons: **Edit**, **Sold out / Available** toggle.

##### Menu item editor

| Field | Type | Required | Rules / notes |
|---|---|---|---|
| Picture | image (Choose picture... / Remove picture) | No | JPEG or PNG up to 2 MB; shrunk to 512 pixels. Can be added after the item is first saved. |
| Name | text | Yes | Up to 100 characters. |
| Price | ₹ amount | Yes | 0 to ₹10,00,000, at most 2 decimals (for example 180 or 180.50). |
| Code (optional) | text | No | Up to 20; letters, digits, `-`, `_`. |
| Category | dropdown | Yes | |
| Tax | dropdown | No | Taxes defined in the Taxes tab (for example GST 5%). |
| Prepared at | dropdown | Yes | Preparation station; kitchen tickets are routed to it. |
| Description (optional) | text | No | Up to 500 characters. |
| HSN/SAC code (optional) | text | No | 4 to 8 digits. Empty uses the restaurant default (996331 for restaurant service). |
| Sort order in its category | number | Yes | Whole number 0 to 9999. |
| Available (untick when sold out) | checkbox | - | |
| Active (on the menu) | checkbox | - | |
| Modifier groups offered with this item | list of checkboxes | No | Each shows its name and a summary. |

Buttons: **Close**, **Create / Save**. If someone else changed the item meanwhile the screen offers the newer version.

#### 6.6.2 Categories tab
Columns: ORDER, NAME, ACTIVE ITEMS, STATUS. Buttons: **New category**, **Edit**, refresh.

##### Category editor
| Field | Type | Required | Rules / notes |
|---|---|---|---|
| Name | text | Yes | Up to 100 characters. |
| Sort order (position of the tab when ordering) | number | Yes | 0 to 9999. |
| Active | checkbox | - | Deactivating a category that still has active items asks whether to deactivate it and all its items. |

#### 6.6.3 Modifiers tab
Two lists: modifier groups (columns GROUP, SELECTION, OPTIONS, STATUS) and, for the selected group, its options (columns ORDER, OPTION, PRICE CHANGE, STATUS). Buttons: **New group**, **Edit group**, **Add option**, **Edit option**, refresh.

##### Modifier group editor
| Field | Type | Required | Rules / notes |
|---|---|---|---|
| Name | text | Yes | Up to 100, e.g. Spice level. |
| Minimum to pick | number | Yes | 0 or more; two digits. |
| Maximum to pick | number | Yes | 1 to 20; not less than the minimum. |
| Active | checkbox | - | |

##### Modifier option editor
| Field | Type | Required | Rules / notes |
|---|---|---|---|
| Name | text | Yes | Up to 100, e.g. Extra cheese. |
| Price change | ₹ amount | Yes | Can be negative (for example 30, 0, -10); within plus or minus ₹1,00,000; at most 2 decimals. |
| Sort order | number | Yes | 0 to 9999. |
| Active | checkbox | - | |

#### 6.6.4 Taxes tab (administrators only)
Note on screen: a new rate applies to items ordered from then on; existing orders keep their rate. Columns: NAME, CODE, RATE %, STATUS. Buttons: **New tax**, **Edit**.

##### Tax editor
| Field | Type | Required | Rules / notes |
|---|---|---|---|
| Name (printed on invoices) | text | Yes | Up to 100. |
| Code | text | Yes | Up to 20; letters, digits, `-`, `_`. |
| Rate (%) | number | Yes | 0 to 100, up to 2 decimals. |
| Active | checkbox | - | |

#### 6.6.5 Stations tab (administrators only)
Kitchen tickets are routed to the station that prepares each item. Columns: ORDER, NAME, CODE, STATUS. Buttons: **New station**, **Edit**.

##### Station editor
| Field | Type | Required | Rules / notes |
|---|---|---|---|
| Name | text | Yes | Up to 100. |
| Code (shown on kitchen tickets) | text | Yes | Up to 20; letters, digits, `-`, `_`. |
| Sort order | number | Yes | 0 to 9999. |
| Active | checkbox | - | |

#### 6.6.6 Preview tab
Read-only view of the menu exactly as the ordering terminals receive it (category list, item tiles with price, "+ options", SOLD OUT) and the menu version. Updates by itself.

Source: `Modules/MenuAdmin/MenuAdminView.xaml`, `MenuItemsPageView.xaml`, `CategoriesPageView.xaml`, `ModifiersPageView.xaml`, `TaxesPageView.xaml`, `StationsPageView.xaml`, `MenuPreviewView.xaml`

### 6.7 Discounts & Payments

Purpose: predefined discounts and payment methods; the cash denominations. Opened by Admin, Manager. Tabs: **Discounts & payments**, **Denominations**.

#### 6.7.1 Discounts (on the first tab)
Note: cashiers pick these on the bill; mark a discount as needing a manager when it is large. Columns: NAME, VALUE, APPROVAL (Manager or none), STATUS. Buttons: **New discount**, **Edit**.

##### Discount editor
| Field | Type | Required | Rules / notes |
|---|---|---|---|
| Name | text | Yes | Up to 100. |
| Type | choice | Yes | Percent of the bill, or Fixed amount. |
| Value | number | Yes | More than 0, at most 2 decimals; a percentage must not exceed 100; an amount must not exceed ₹1,00,00,000. |
| Needs manager approval | checkbox | - | |
| Active | checkbox | - | |

#### 6.7.2 Payment methods (on the first tab)
Note: CASH computes change; methods with a reference ask for the card slip or UPI code. Columns: NAME, CODE, REFERENCE, ORDER, STATUS. Buttons: **New method**, **Edit**.

##### Payment method editor
| Field | Type | Required | Rules / notes |
|---|---|---|---|
| Name | text | Yes | Up to 100. |
| Code | text | Yes | Up to 20; letters, digits and `_` only. |
| Order on the payment screen | number | Yes | Whole number 0 to 9999. |
| Ask for a reference number | checkbox | - | |
| Active | checkbox | - | |

#### 6.7.3 Denominations tab
Heading "Notes and coins": counted when a shift opens and closes. Deactivate a note that left circulation; past counts keep it.
Grid (editable): LABEL, VALUE, COIN, ORDER, ACTIVE. Buttons: **Add**, **Save**.
Rules: each row needs a label (up to 20 characters) and a value above zero (up to ₹1,00,000); at most 40 denominations.

Source: `Modules/Billing/BillingSetupView.xaml`, `Modules/Cash/DenominationsView.xaml`

### 6.8 Audit Log

Purpose: who changed what, when, from which terminal. Opened by Admin, Manager (permission "view audit trail").

| Field | Type | Required | Rules / notes |
|---|---|---|---|
| Date range | dropdown + From/To | Yes | Same presets as Reports. |
| Search | text | No | User, action or terminal contains. |
| User | text | No | Filter. |
| Terminal | text | No | Filter. |
| Action | text | No | Exact action name, for example Order.Cancelled. |

Buttons: **Search** (or Enter), refresh, **Export CSV**, **Export Excel**, **Save PDF**, previous/next page.
Columns: TIME, ACTION, USER, TERMINAL, ENTITY, ID. Selecting a row shows who, on which terminal, the target, and Before / After values.

Source: `Modules/Admin/AuditLogsView.xaml`

---

## 7. Administration

### 7.1 Users

Purpose: staff accounts, roles and passwords. Opened by Admin (permission "manage staff accounts").
Controls: search (username or name), Show inactive, refresh, **New user**, paging (50 per page).
Columns: USERNAME, NAME, ROLES, STATUS, LOCKOUT (Normal or Locked Out), LAST SIGN-IN.
Buttons for the selected user: **Edit**, **Reset password**, **Deactivate / Activate**, **Unlock**, **Sessions**, **Log out everywhere**.
- Reset password: a pop-up asks for a temporary password (must meet the password rule); the person must change it at next sign-in and their open sessions are signed out.
- Deactivate: asks to confirm; the person is signed out and cannot sign in until reactivated.
- Sessions: shows the active sessions (terminal, IP address, signed in, expires).
- Log out everywhere: asks to confirm; ends all sessions on all terminals.

#### 7.1.1 User editor

| Field | Type | Required | Rules / notes |
|---|---|---|---|
| Username | text | Yes (new only; cannot be changed later) | 3 to 50 characters; letters, digits, `.`, `-`, `_`; no spaces. |
| Display name | text | Yes | Up to 100. Shown on screens and tickets. |
| Initial password | password | Yes (new only) | Must meet the password rule (the rule is shown below the field). |
| Must change password at first sign-in | checkbox | - | On by default; new users only. |
| Roles | list of checkboxes with description | Yes (at least one) | Admin, Manager, Cashier, Waiter, Kitchen. |

Buttons: **Cancel**, **Create / Save**.

Source: `Modules/Admin/UsersView.xaml`, `Modules/Admin/UserEditorViewModel.cs`

### 7.2 Roles & Permissions

Purpose: fine-grained permissions per role. Opened by Admin.
Left: ROLES list (name, "System" tag, description). Right: the selected role and a permission list grouped as Tables, Menu, Orders, Kitchen, Billing, Cash, Management, Administration, GST. The Admin role is fixed ("Protected: System Administrator has all permissions"); a padlock marks the core permissions of each system role, which cannot be removed.

| Field | Type | Required | Notes |
|---|---|---|---|
| Permission (one per line) | checkbox | - | Examples: View floor plan and tables; Occupy and release tables; Configure sections and tables; Browse menu catalog; Manage items, prices, and categories; Toggle item availability; Create and submit orders; Edit orders placed by other staff; Cancel orders or items placed by others; Manage tickets on Kitchen Display; Claim bills, add payments, close orders; Apply discounts; Void bills; Issue refunds; Open and close own cash shift; View and close any shift; Run the day-end close; View sales and operational reports; View system audit trail; Create and restore database backups; Manage staff accounts; Edit roles; Modify restaurant system settings; Manage authorized terminals and stations; Server status, update hosting; Manage the licence; Import data; View GST reports; Close and reopen GST periods. |
| Force re-login for active users with this role | checkbox | - | Revokes their sessions so they pick up the new permissions. |

Buttons: refresh, **Save permissions**.

Source: `Modules/Admin/RolesView.xaml`

### 7.3 Devices

Purpose: registered terminals and which are online. Opened by Admin.
Columns: LIVE (green dot), NAME, TYPE, STATION, MACHINE, VERSION, STATUS (Online / Offline), LAST SEEN.
Buttons for the selected terminal: **Rename terminal** (pop-up; same name rules as a terminal name: 2 to 50, letters, digits, `-`, `_`), **Deactivate / Activate** (deactivating asks to confirm; the terminal is disconnected at once and its requests are blocked), **Assign station** with a **Kitchen station** dropdown (which station's tickets a kitchen terminal shows).

Source: `Modules/Admin/DevicesView.xaml`

### 7.4 Licence

Purpose: trial or licence status. Opened by Admin (permission "manage the licence").
Shows: state (Licensed, Trial with days left, or Expired: read-only), a message, Licensed to, Edition, Terminals (allowed), Valid until, and the Machine id (**Copy** button) - send it to your supplier; a key only works on the PC it was issued for.

| Field | Type | Required | Rules / notes |
|---|---|---|---|
| Licence key | text (paste) | Yes | Starts with `HPOS1-`; paste exactly as received. Takes effect at once. |

Button: **APPLY KEY**.

Source: `Modules/Licensing/LicenseView.xaml`

### 7.5 Restaurant Settings

Purpose: shared settings for every terminal and the print templates. Opened by Admin (permission "modify system settings"). Tabs: **Restaurant settings**, **Print templates**.

#### 7.5.1 Restaurant settings tab
A single alphabetical list, one row per setting: name, description, an edit box (or an Enabled checkbox for yes/no settings), an "Edited" mark for unsaved changes, and a hint under the box for number settings ("Whole number", "Number", "Time, HH:mm"). Changes are audited. Buttons: reload icon (discards unsaved edits), **Save changes**. The server rejects a value that is not valid for the setting's type (whole number, number, time, yes/no) and shows the message beside that setting. The only range check found in code is the password minimum length; other limits in the descriptions below are guidance and may not be enforced.

| Setting (as shown) | Type | Default | What it does |
|---|---|---|---|
| Restaurant name | text | HotelPOS Restaurant | Printed on invoices and shown on all terminals. |
| Address | text | empty | Printed on invoices. |
| Phone | text | empty | Printed on invoices. |
| Gstin | text | empty | The restaurant's GSTIN, printed on invoices and used in GST returns. (No format check was found when saving this setting; enter 15 characters carefully.) |
| Currency symbol | text | ₹ | Shown on screens and documents. |
| Invoice prefix | text | INV- | Start of invoice numbers. |
| Invoice reset policy | text | Yearly | When numbering restarts: Never, Yearly or Monthly. |
| Round off totals | yes/no | yes | Round bill totals to whole rupees. |
| Business day start time | time HH:mm | 04:00 | When a new business day starts. |
| Kitchen warn minutes | whole number | 10 | Kitchen ticket turns amber. |
| Kitchen late minutes | whole number | 20 | Kitchen ticket turns red. |
| Auto close on full payment | yes/no | yes | Closes the order and frees the table when fully paid. |
| Receipt footer | text | Thank you! Visit again. | Footer on invoices and receipts. |
| Tax split display | text | CGST_SGST | How tax shows on invoices: SINGLE or CGST_SGST. |
| Allow any waiter to edit orders | yes/no | no | Lets any waiter change or cancel another waiter's order. |
| Max cashier discount percent | number | 10 | Largest discount a cashier may give without manager approval. |
| Allow bill before ready | yes/no | no | Request a bill before every kitchen ticket is ready (drinks-only tables). |
| Print invoice copies | whole number | 1 | Copies printed on finalizing (1 to 5 by description). |
| Kot show prices | yes/no | no | Print prices on kitchen tickets. |
| Lockout threshold | whole number | 5 | Failed sign-ins before an account locks. |
| Lockout minutes | whole number | 15 | How long a locked account stays locked. |
| Audit retention days | whole number | 0 | Delete audit entries older than this (0 keeps all). |
| Password min length | whole number | 8 | Shortest password for new and reset passwords; must be 8 to 100. |
| Password require letter | yes/no | yes | New passwords must contain a letter. |
| Password require digit | yes/no | yes | New passwords must contain a digit. |
| Password require upper and lower | yes/no | no | Must contain both upper and lower case letters. |
| Password require symbol | yes/no | no | Must contain a symbol such as ! or #. |
| Upi vpa | text | empty | UPI ID for the payment QR on invoices; empty prints no QR. |
| Upi payee name | text | empty | Payee name in the QR; empty uses the restaurant name. |
| Logo file | text | empty | Managed by the Print templates tab (use Upload logo there). |
| Pdf paper | text | A4 | Paper for invoice and receipt PDFs when the template uses a thermal roll: A4, A5 or 80mm. |
| Default sac code | text | 996331 | SAC code printed for restaurant service. |
| Gst b2cl threshold | number | 100000 | Inter-state invoice to an unregistered buyer above this is reported as B2CL. |
| Backup enabled | yes/no | yes | Daily automatic backup. |
| Backup time | time HH:mm | 03:30 | When the daily backup runs. |
| Backup retention days | whole number | 14 | Delete backups older than this (at least 1). |
| Backup path | text | empty | Backup folder; empty uses the server default. |
| Cash variance approval limit | number | 100 | A shift's cash difference above this needs manager approval to close. |
| Cash pay out approval limit | number | 1000 | Pay-outs above this need manager approval. |
| Cash blind count | yes/no | no | Cashiers count before seeing the expected cash. |

The Menu version setting is kept by the system and is not shown. The password-rule settings apply to the Change password screen, the new user editor and Reset password.

**Notifications on this terminal** (below the settings list, Phase 20.2). One row per kind of notification (new kitchen ticket, order ready, ticket running late, bill requested, bill claimed, payment completed, device online/offline, server message, GST period change, day end due, print failed, payment failed) with two checkboxes: **Pop-up** (show the toast) and **Sound** (play a sound). These are kept for this terminal and Windows profile in `preferences.json`, are not shared with other terminals and are not part of **Save changes**: each tick is saved at once. The bell list always keeps the item, even with Pop-up off. Until a row is changed it follows the default: Pop-up on for every kind; Sound on only for a new kitchen ticket on a kitchen terminal, a bill request on a cashier terminal, and a failed print on every terminal. The browser and Android app have the same list on their Settings page (route `settings`) with an extra **Test sound** button; there Sound also vibrates where the phone can. Because this Settings screen is Admin only, every other role opens the same list from the **Notifications…** bell icon in the sidebar user block. In the browser and Android app the bottom navigation of the waiter (Home, Tables, My orders) and of the kitchen (Kitchen, Completed, Station) ends with a **Settings** link to `settings`; the bell announces the unread count to screen readers through hidden live text (for example "3 unread notifications").

#### 7.5.2 Print templates tab (Template Studio)
Purpose: design the kitchen ticket, invoice and receipt. Each save creates a numbered version; one version is active.

Top controls:
| Field | Type | Required | Rules / notes |
|---|---|---|---|
| Document | dropdown | Yes | Kitchen ticket, Invoice, Receipt. |
| Preview with | dropdown | Yes | Sample bill, or a recent real bill. |
| Version name | text | Yes to save | Name of the next saved version; up to 80 characters. |

Buttons: **Print test**, **Save version**, **Save and activate**, **Activate** (make the selected older version active, to roll back). The Versions list shows each version, which is Active, and its detail. A live preview is shown on the right.

Layout options (what shows depends on the document):
| Option | Type | Shown for | Notes |
|---|---|---|---|
| Paper | dropdown | all | 58 mm roll, 80 mm roll, A5 sheet, A4 sheet. |
| Large text on thermal paper | checkbox | all | |
| Logo | checkbox + **Upload logo...** / **Remove logo** | invoice, receipt | Logo file up to 2 MB, 600 pixels. |
| Address, Phone | checkboxes | invoice | |
| GSTIN (required on a GST invoice) | checkbox | invoice | |
| Header lines (one per line) | multi-line text | invoice | Up to 6 lines of 120 characters. |
| Columns | checkbox + up/down arrows per column | invoice | Item, HSN / SAC, Quantity, Rate, Discount, GST %, Amount. Item, quantity and amount are required on a GST invoice. |
| GST breakup style | dropdown | invoice | As the tax setting says; CGST and SGST rows; Rate-wise table; One GST line. |
| UPI payment QR | checkbox | invoice | UPI ID comes from Restaurant settings. |
| Waiter, Guests | checkboxes | invoice and kitchen ticket | |
| Customer, Payments | checkboxes | invoice | |
| Kitchen ticket prices | dropdown | kitchen ticket | As the KOT prices setting says; Show prices; Hide prices. |
| Notes in bold, Station heading | checkboxes | kitchen ticket | |
| Footer lines, Terms (one per line) | multi-line text | invoice, receipt | Up to 6 lines of 120 characters. |
| Printed date and time | checkbox | invoice, receipt | |

Source: `Modules/Admin/SettingsHubView.xaml`, `SettingsView.xaml`, `TemplateStudioView.xaml`

### 7.6 Backup

Purpose: scheduled and manual database backups. Opened by Admin (permission "create and restore database backups").
Note: a verified copy of the database and the menu pictures, taken daily on the server; copy the folder to a USB drive or NAS regularly. A red text shows the last failure.

| Field | Type | Required | Rules / notes |
|---|---|---|---|
| Folder on the server (empty = default) | text | No | |
| Daily at (HH:mm) | time | Yes | 24-hour HH:mm, for example 03:30. |
| Keep for (days) | whole number | Yes | At least 1. |
| Daily backup enabled | checkbox | - | |

Buttons: **BACKUP NOW**, **Save settings**, refresh. List columns: STARTED, STATUS, SIZE, DURATION, TRIGGER (manual or scheduled), FILE, ERROR.

Source: `Modules/Admin/BackupView.xaml`

### 7.7 Server

Purpose: send a notice to every terminal and read the server log. Opened by Admin (permission "server status").

| Field | Type | Required | Rules / notes |
|---|---|---|---|
| Notice level | dropdown | Yes | Info, Warning, Critical. |
| Message to every connected terminal | text | Yes | Up to 300 characters, for example "Server restarting in 2 minutes for maintenance". |

Buttons: **Send notice** (terminals show it in the Notices bell). Server log: level filter (All levels, Warnings, Errors, Fatal), number of lines (100, 500, 2000), refresh; the log lines are listed below.

Source: `Modules/Admin/ServerView.xaml`

### 7.8 This Terminal

The same screen as the first-run configuration (2.1), reached any time from the sidebar by Admin or Manager. The settings are saved on this PC only.

### 7.9 Printers

Purpose: kitchen, invoice and receipt printers attached to this PC. Opened by Admin, Manager. Settings are saved on this PC only. Thermal printers on USB need the "Generic / Text Only" Windows driver.
One block each for Kitchen ticket (kitchen), Invoice and Receipt:

| Field | Type | Required | Rules / notes |
|---|---|---|---|
| Enabled | checkbox | - | Off removes the printer for that document. |
| Connection | dropdown | Yes | EscPosRaw (USB/spooled thermal), Network (IP printer), Windows (normal Windows printer). |
| Windows printer name | dropdown of installed printers (editable) | Yes for spooled connections | |
| Printer IP address | text | Yes for Network | |
| Port | number | Yes for Network | 1 to 65535; default 9100. |
| Paper width (mm) | dropdown | Yes (thermal) | 58 or 80. |
| Copies | dropdown | Yes | 1 to 5. |
| Printer model | dropdown | Yes (thermal) | Generic, Epson, Xprinter. |
| Print ₹ as | text | No | Replacement text for the rupee sign when the printer cannot print it. |
| Print automatically | checkbox | - | Kitchen ticket: when a ticket is created (kitchen terminals); invoice: when finalized. Not offered for receipts. |

Buttons: **TEST PRINT** (shows the result under it), **Save printer settings**. A red message appears on any block that needs fixing before saving. (Messages: "Enter the printer's network address", "Choose or type the Windows printer name", "Enter a port between 1 and 65535".)

Source: `Modules/Printing/PrinterSettingsView.xaml`; the print preview pop-up (`PrintPreviewView.xaml`) has **CLOSE** and **PRINT** and names the target printer.

---

## Appendix A. Roles summary

Default audience of each menu. Administrators can change these under Roles & Permissions, after which the sidebar follows the saved permissions.

| Menu / screen | Admin | Manager | Cashier | Waiter | Kitchen |
|---|---|---|---|---|---|
| Service > Dashboard | Yes | Yes | - | Yes | - |
| Service > Dashboard (cashier home) | - | - | Yes | - | - |
| Service > Tables | Yes | Yes | Yes | Yes | - |
| Service > My Orders | Yes | Yes | - | Yes | - |
| Kitchen > Kitchen Display | Yes | Yes | - | - | Yes |
| Kitchen > Completed Orders | Yes | Yes | - | - | Yes |
| Billing > Billing | Yes | Yes | Yes | - | - |
| Billing > Closed Bills | Yes | Yes | Yes | - | - |
| Billing > Customers | Yes | Yes | Yes | - | - |
| Billing > Cash Shifts | Yes | Yes | - | - | - |
| Billing > Day End | Yes | Yes | - | - | - |
| Billing > Day End History | Yes | Yes | - | - | - |
| Management > Reports | Yes | Yes | - | - | - |
| Management > Tax Returns | Yes | Yes | - | - | - |
| Management > Period Close | Yes | - | - | - | - |
| Management > Import | Yes | Yes | - | - | - |
| Management > Sections & Tables | Yes | Yes | - | - | - |
| Management > Menu | Yes | Yes (Taxes and Stations tabs: Admin only) | - | - | - |
| Management > Discounts & Payments | Yes | Yes | - | - | - |
| Management > Audit Log | Yes | Yes | - | - | - |
| Administration > Users | Yes | - | - | - | - |
| Administration > Roles & Permissions | Yes | - | - | - | - |
| Administration > Devices | Yes | - | - | - | - |
| Administration > Licence | Yes | - | - | - | - |
| Administration > Restaurant Settings | Yes | - | - | - | - |
| Administration > Backup | Yes | - | - | - | - |
| Administration > Server | Yes | - | - | - | - |
| Administration > This Terminal | Yes | Yes | - | - | - |
| Administration > Printers | Yes | Yes | - | - | - |

Home screen after sign-in: Admin and Manager - Dashboard; Cashier - cashier home; Kitchen - Kitchen Display; Waiter - Tables.
Cash shift bar: billing-type terminals, for users with the "open and close own cash shift" permission (and administrators).
Manager approval: a Manager (or Admin) username and password; Admin and Manager can approve their own actions.

## Appendix B. Things the code does not settle

- Cashier's Dashboard: resolved in Phase 20.6; a Cashier gets the cashier home (3.1.1). The waiter-style own-figures view remains for the Waiter role.
- Settings values other than the password minimum length are checked only for type on the server; the ranges in the descriptions (for example invoice copies 1 to 5) may be enforced elsewhere or not at all.
- The GSTIN entered under Restaurant Settings has no format check in the settings screen; GSTINs on the bill and the customer editor are fully checked.
- Phone numbers are not limited to 10 digits anywhere; the rule is digits, spaces, `+` and `-`, up to 20 characters.
