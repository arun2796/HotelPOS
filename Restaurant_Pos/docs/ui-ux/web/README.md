# Web client (waiter + kitchen) UI/UX gallery

Captured 2026-10-16 against the API in Development mode (`http://localhost:5000/app`, demo seed), headless Microsoft Edge
through Playwright, `deviceScaleFactor: 2`. Waiter: phone 390 x 844 (touch, mobile). Kitchen: tablet 1024 x 768 landscape.
Accounts `waiter1` and `kitchen1`. Order #1005 on table T05 was created by this flow and left with the **bill requested**.

Script: [`../tools/web-flow.mjs`](../tools/web-flow.mjs) (how to run it is in its header). Two browser-only workarounds
are built in and are themselves findings: the stylesheet path rewrite (issue 3) and tapping again after a toast (issue 1).

## Screenshots

| Image | What it shows | Viewport |
|---|---|---|
| `waiter/00-dev-mode-unstyled-sign-in.png` | Sign-in as served by `dotnet run`, before the CSS workaround: no styles, "Something went wrong" bar visible | phone |
| `waiter/01-waiter-sign-in.png` | Sign in, Waiter selected, credentials filled | phone |
| `waiter/02-waiter-home.png` | `/home`: My tables, Ready, Bill requests (empty states) | phone |
| `waiter/03-waiter-tables.png` | Tables map, section chips, coloured tiles | phone |
| `waiter/04-waiter-table-details.png` | T05 available, Seat guests stepper set to 2, OCCUPY and NEW ORDER | phone |
| `waiter/04b-waiter-table-occupied.png` | T05 after OCCUPY (taken on a re-run, so already occupied) | phone |
| `waiter/05-waiter-order-search.png` | Order builder, search "biry", two items pinned with the star | phone |
| `waiter/06-waiter-favourites.png` | ★ Favourites tab with the two pinned biryanis | phone |
| `waiter/07-waiter-item-options.png` | Options sheet for Chicken Biryani: Add-ons (Extra raita) + Spice level (Mild) | phone |
| `waiter/07c-waiter-sheet-stuck-after-add.png` | Same sheet after tapping "Add to order": still open (issue 1) | phone |
| `waiter/07b-waiter-kitchen-note.png` | Note sheet: quick notes + free text "Less oil, extra raita on the side" | phone |
| `waiter/08-waiter-cart.png` | Cart: biryani (options + note), Butter Naan x2, Masala Chai (Less sugar); toast and error bar over SEND | phone |
| `waiter/08b-waiter-cart-fullpage.png` | Same, full page | phone |
| `waiter/09a-waiter-send-stuck-on-builder.png` | After SEND TO KITCHEN: toast "Order #1005 sent" but the page stays on the builder with the old cart (issue 1) | phone |
| `waiter/09-waiter-after-send.png` | T05 details: order #1005, lines "New", ADD ITEMS / CANCEL ORDER | phone |
| `waiter/09b-waiter-my-orders.png` | My orders after send | phone |
| `waiter/10-waiter-bell-panel.png` | Notification panel open (empty) | phone |
| `waiter/11-waiter-settings.png` | Settings: notification kinds with Pop-up / Sound | phone |
| `waiter/12-waiter-tables-dark.png` | Tables in dark theme (toggle) | phone |
| `kitchen/13-kitchen-sign-in.png` | Sign in, Kitchen selected | tablet |
| `kitchen/14-kitchen-board-new.png` | Kitchen Display, NEW column with the T05 tickets (one per station) | tablet |
| `kitchen/14b-kitchen-board-new-scrolled.png` | Same, scrolled to the second T05 ticket | tablet |
| `kitchen/15-kitchen-station.png` | Station page (reached by URL, issue 2) | tablet |
| `kitchen/16a-kitchen-accepted.png` | Both T05 tickets accepted | tablet |
| `kitchen/16-kitchen-preparing.png` | Both T05 tickets in PREPARING; the note shows in orange italic | tablet |
| `kitchen/17-kitchen-ready.png` | Both T05 tickets in READY with DONE / RECALL | tablet |
| `kitchen/18-kitchen-completed.png` | Completed page, **loading skeleton only** (see "Not captured") | tablet |
| `kitchen/19-kitchen-bell-panel.png` | Kitchen notification panel (empty) | tablet |
| `kitchen/20-kitchen-dark.png` | Kitchen Display, dark theme | tablet |
| `waiter/21-waiter-my-orders-ready.png` | My orders: #1005 highlighted, chip "Ready" (red) | phone |
| `waiter/22a-waiter-table-ready.png` | T05 details with lines Ready, SERVED / REQUEST BILL | phone |
| `waiter/22-waiter-bill-requested.png` | After REQUEST BILL: "Bill requested — the counter has it." | phone |
| `waiter/23-waiter-bell-ready.png` | Bell panel after Ready + bill request: still "No notifications" | phone |
| `waiter/24a-waiter-offline-no-banner-yet.png` | Network off, pill "Reconnecting…", but no offline banner (issue 4) | phone |
| `waiter/24-waiter-offline-banner.png` | Offline banner after the next tap | phone |
| `waiter/25-waiter-offline-my-orders.png` | Natural error state: My orders while offline, "The server cannot be reached… Retry" | phone |
| `waiter/24b-waiter-back-online.png` | Back online: pill Connected, list reloaded by itself, #1005 "Bill requested" | phone |

## Issues found while driving

Paths are under `src/HotelPOS.Client.Shared/`. Contrast ratios and target sizes were measured in the page
(WCAG 4.5:1 for normal text, 44 px targets per the project's own rule in `wwwroot/css/app.css`).

### Functional (fix first)

1. **Every toast throws in the browser build.** `ToastService.Add` (`Services/ShellServices.cs`) calls
   `TaskScheduler.FromCurrentSynchronizationContext()`; Blazor WebAssembly has no SynchronizationContext, so it throws
   `InvalidOperationException: The current SynchronizationContext may not be used as a TaskScheduler` (seen in the
   console). Effects: the "Something went wrong · Reload" bar appears (08, 09a); toasts never auto-dismiss and sit over
   the SEND button (08); and the code after the toast never runs, so the page does not re-render:
   the options sheet stays open after "Add to order" (07c), tapping a plain item looks like nothing happened,
   **SEND TO KITCHEN sends the order but leaves the waiter on the builder with the old cart** (09a, a double-send risk),
   and Table details skips its reload after Occupy / Request bill (`RunAsync`), leaving stale, greyed buttons (23).
   Pages: `Pages/Waiter/OrderBuilderPage.razor` (Pick, AddFromPicker, SendAsync, SaveDraftAndBackAsync),
   `Pages/Waiter/TableDetailsPage.razor` (RunAsync), `Shared/Toasts.razor`. bUnit tests would not see it (they have a context).
2. **The kitchen user has no bottom navigation.** `Layout/MainLayout.razor`: `IsKitchen` requires *not* `TablesView`,
   but the default Kitchen role has `TablesView` (`Contracts/Security/RolePermissionMap.cs`), and `IsWaiter` excludes the
   Kitchen role, so neither nav renders. A kitchen tablet cannot reach Completed, Station or Settings except by typing the
   URL (14–20).
3. **Unstyled app under `dotnet run`.** `index.html` asks for `/app/_content/HotelPOS.Client.Shared/css/*.css`; in
   Development those files are served at `/_content/...` and `/app/_content/...` answers **401 JSON**, so the app
   renders with browser defaults and the error bar shows (00). The published layout (`wwwroot/app/_content`) was not
   checked here. The script rewrites the URL in the browser only (`HOTELPOS_NO_CSS_FIX=1` turns that off).
4. **The offline banner does not appear when the connection drops.** The pill changes to "Reconnecting…" but
   `OrderBuilderPage.razor` does not re-render on a realtime status change; the banner shows only after the next tap
   (24a vs 24).
5. **No "Order ready" notification reached the waiter.** After the kitchen pressed READY on both tickets and the bill
   was requested, the bell still said "No notifications" (23) although "Order ready" pop-up is on (11). My orders did
   update live (21). Possibly the same toast exception inside the notification path; not confirmed.
   `Shared/NotificationPanel.razor`, `Services/NotificationRouter.cs`.
6. **Each browser profile uses a licence seat.** The device identity is per browser storage, so a cleared browser,
   private window or a test run registers a new `WEB-WAITER-xxxxxx` / `WEB-KITCHEN-xxxxxx`. This session's runs used
   about 13 seats and the demo licence (25) is now full: new sign-ins get "This licence covers 25 terminal(s) and 25 are
   registered…". The message is clear, but there is no self-clean-up of never-seen-again browser terminals.
   The script now keeps the identity between runs (`STATE_DIR`).

### Feedback, labels and consistency

7. Three status words for one order on one screen: table "Preparing", order chip "Submitted", lines "New" (09); Home
   says "Submitted" while the map says "Preparing" (02/03). Raw enum text "BillRequested" (23) where My orders says
   "Bill requested" (24b). `Pages/Waiter/TableDetailsPage.razor` prints `@order.Status` / `@table.Status` directly.
8. "Ready" is a **red** (danger) chip (21; also Home). Red reads as an error; ready-to-serve should look positive.
   `Pages/Waiter/MyOrdersPage.razor`, `Pages/Waiter/HomePage.razor`.
9. Avatar shows "A(" / "R(" (every screen): initials are taken from "Arun (Waiter)". `Layout/MainLayout.razor` `Initials`.
10. Top-bar text is cut: "Your tables, ready (" (02), "Your active orders;" (09b, 21), "New or…" when the pill grows to
    "Reconnecting…" (24). `Layout/MainLayout.razor` / `.topbar` in `app.css`.
11. Plurals: "1 guests" (Home, My orders, 24), "Order · 1 items" (07b). `Shared/Stepper.razor` suffix,
    `OrderBuilderPage.razor`, `HomePage.razor`, `MyOrdersPage.razor`.
12. While searching, the "Starters" chip stays highlighted although the search ignores the tabs (05).
    `OrderBuilderPage.razor`.
13. The kitchen note cannot be added in the options sheet; it needs a second sheet via the ✎ on the cart line
    (07 → 07b). "Add to order" wraps to two lines and the quantity stepper in the sheet has no label (07).
14. Table details for an available table shows OCCUPY and a full-width NEW ORDER at the same time with no hint which
    comes first (04). CANCEL ORDER is a full-width solid red button directly under ADD ITEMS (09); after the bill is
    requested ADD ITEMS and CANCEL ORDER are still offered (23, partly stale because of issue 1).
    `Pages/Waiter/TableDetailsPage.razor`.
15. Settings shows 15 notification kinds to a waiter, including desktop-only ones (GST period, Day end is due, Print
    failed, Payment failed, Device online/offline); "Pop-up" wraps on long rows; no "saved" feedback (11).
    `Pages/Settings.razor`.
16. Notification panel has no backdrop and covers the page content; the empty state gives no hint what will appear (10, 19).
17. Sign-in button stays disabled after typing until the field loses focus (`@bind` updates on change), so it looks
    broken while the password field has focus; Enter and a tap still work. `Pages/SignIn.razor`.

### Layout, contrast and touch targets

18. Sticky cart takes about half the phone height with three lines (08); only one row of items stays visible.
    A collapsed "3 items · 420.00 ›" bar would keep the menu usable. `.cart` in `app.css`.
19. Kitchen: ACCEPT and START are identical green buttons side by side (14) — easy to Start by mistake. The two T05
    tickets (one per station) carry the same big "T05"; only the small "1005-1-BAR / 1005-1-MAIN" text tells them
    apart (14b, 16). "Delayed 2" — the most urgent number — is small muted text. `Pages/Kitchen/KitchenPage.razor`.
20. Contrast (measured): kitchen note orange italic **2.15:1** (16, 17 — the one line the cook must not miss); "Bill
    requested" warning chip **2.01:1** (Home); "Completed" success chip 3.0:1; "Submitted" primary chip 4.0:1; white on
    the primary green 4.39:1 on every primary button (just under 4.5:1 at 15–17 px); the active bottom-nav label 4.39:1.
    Dark theme: white on the brighter green buttons looks lower still (12, 20). Tokens in `wwwroot/css/tokens.css`.
21. Targets under 44 px: section / category / station chips 40 px tall (`.chip-btn` min-height 40, 03, 05, 14); sign-in
    Waiter/Kitchen toggle 150 x 38 (01); Settings checkboxes 13 x 13 (the label is tappable, but the visible box is tiny, 11).
22. Chip rows overflow with a hard cut ("Outdo…", "Brea…") and no fade or arrow to show they scroll (03, 05).

### Worked well

Offline error state with a plain message and Retry (25); automatic reload after reconnect (24b); kitchen tickets are
large and readable at arm's length with 52 px buttons (16, 17); quick notes and the late (red) ticket border are clear;
dark theme covers every element checked.

## Not captured

- **18 Completed (with tickets):** the shot caught the loading skeleton (the page was opened by URL right after a full
  reload). A separate check showed it loads in about 2 s with 4 cards, but a retake was refused by the licence limit
  (issue 6). No ticket was pressed DONE, so T05 would not have been listed anyway; the order stays Ready / bill requested.
- **"Claimed" notice (23):** none arrived while the flow ran.
- `04` shows T05 before OCCUPY from the first styled run; later runs reused the occupied table (`04b`).
