# Web client (waiter + kitchen) UI/UX gallery

First captured 2026-10-16 against the API in Development mode (`http://localhost:5000/app`, demo seed). **Regenerated
2026-10-16 after the Phase 20.11 fixes (commit 368605b)** against a worktree API on `http://localhost:5100/app` (same
dev database), headless Microsoft Edge through Playwright, `deviceScaleFactor: 2`. Waiter: phone 390 x 844 (touch,
mobile). Kitchen: tablet 1024 x 768 landscape. Accounts `waiter1` and `kitchen1`. Order #1009 on table **T06** was created
by this run and left with the **bill requested** (the first run's #1005 on T05 is still open, so it appears on the boards).

Script: [`../tools/web-flow.mjs`](../tools/web-flow.mjs) (how to run it is in its header). This run used
`HOTELPOS_NO_CSS_FIX=1` (no stylesheet rewrite) and no "tap again" nudge was needed: the stuck-page shots
(07c, 09a, 22b, 24a) are taken only when the defect appears, and none was.

## Screenshots

| Image | What it shows | Viewport |
|---|---|---|
| `waiter/01-waiter-sign-in.png` | Sign in, Waiter selected, styled without any workaround (issue 3) | phone |
| `waiter/02-waiter-home.png` | `/home`: My tables, Ready, Bill requests, status chips in the shared words | phone |
| `waiter/03-waiter-tables.png` | Tables map, 44 px section chips with an edge fade, coloured tiles | phone |
| `waiter/04-waiter-table-details.png` | T06 available, Seat guests stepper "2 guests", OCCUPY and NEW ORDER | phone |
| `waiter/04b-waiter-table-occupied.png` | T06 after OCCUPY, refreshed by itself | phone |
| `waiter/05-waiter-order-search.png` | Order builder, search "biry" (no category chip highlighted), two items pinned | phone |
| `waiter/06-waiter-favourites.png` | ★ Favourites tab with the two pinned biryanis | phone |
| `waiter/07-waiter-item-options.png` | Options sheet for Chicken Biryani: Add-ons (Extra raita) + Spice level (Mild) | phone |
| `waiter/07b-waiter-kitchen-note.png` | Note sheet: quick notes + free text "Less oil, extra raita on the side" (was filed under `kitchen/` by a script bug) | phone |
| `waiter/08-waiter-cart.png` | Cart "Order · 4 items" with Hide, lines scrolling inside a capped area, toast that goes away by itself | phone |
| `waiter/08b-waiter-cart-fullpage.png` | Same, full page | phone |
| `waiter/09-waiter-after-send.png` | Straight after SEND TO KITCHEN: T06 details, order "Sent to kitchen", lines "Sent to kitchen", empty draft | phone |
| `waiter/09b-waiter-my-orders.png` | My orders after send | phone |
| `waiter/10-waiter-bell-panel.png` | Notification panel open (empty before the kitchen acts) | phone |
| `waiter/11-waiter-settings.png` | Settings: notification kinds with Pop-up / Sound, 22 px checkboxes | phone |
| `waiter/12-waiter-tables-dark.png` | Tables in dark theme (toggle) | phone |
| `kitchen/13-kitchen-sign-in.png` | Sign in, Kitchen selected | tablet |
| `kitchen/14-kitchen-board-new.png` | Kitchen Display with its bottom navigation; T06 tickets with a station chip; ACCEPT solid, START outlined | tablet |
| `kitchen/14b-kitchen-board-new-scrolled.png` | Same, scrolled to the second T06 ticket | tablet |
| `kitchen/15-kitchen-station.png` | Station page, reached through the bottom nav | tablet |
| `kitchen/16a-kitchen-accepted.png` | Both T06 tickets accepted ("Accepted" chip) | tablet |
| `kitchen/16-kitchen-preparing.png` | T06 in PREPARING; the note in bold amber with ✎; "Delayed 3" as a red chip | tablet |
| `kitchen/17-kitchen-ready.png` | T06 in READY with DONE (solid) / RECALL (outlined) | tablet |
| `kitchen/18-kitchen-completed.png` | Completed page through the nav, loaded | tablet |
| `kitchen/19-kitchen-bell-panel.png` | Kitchen notification panel | tablet |
| `kitchen/20-kitchen-dark.png` | Kitchen Display, dark theme | tablet |
| `waiter/21-waiter-my-orders-ready.png` | My orders: #1009 first with a green "Ready" chip, "1 guest" plural on #1003 | phone |
| `waiter/22a-waiter-table-ready.png` | T06 details with lines Ready, SERVED / REQUEST BILL | phone |
| `waiter/22-waiter-bill-requested.png` | After REQUEST BILL, reloaded by itself: "Bill requested — the counter has it." | phone |
| `waiter/23-waiter-bell-ready.png` | Bell: "TABLE T06 — ORDER READY", "being prepared", "kitchen accepted" (issue 5) | phone |
| `waiter/24-waiter-offline-banner.png` | Network off: offline banner together with the "Reconnecting…" pill, no tap | phone |
| `waiter/25-waiter-offline-my-orders.png` | Natural error state: My orders while offline, "The server cannot be reached… Retry" | phone |
| `waiter/24b-waiter-back-online.png` | Back online: pill Connected, list reloaded by itself | phone |

Removed after the fixes (they showed defects that no longer occur): `00-dev-mode-unstyled-sign-in`,
`07c-waiter-sheet-stuck-after-add`, `09a-waiter-send-stuck-on-builder`, `24a-waiter-offline-no-banner-yet`.

## Issues found while driving

Paths are under `src/HotelPOS.Client.Shared/`. Contrast ratios and target sizes were measured in the page
(WCAG 4.5:1 for normal text, 44 px targets per the project's own rule in `wwwroot/css/app.css`).
"Fixed (368605b)" means fixed in that commit and seen in the named new screenshot.

### Functional (fix first)

1. **Fixed (368605b)** — 07, 08, 09, 22, 04b. **Every toast threw in the browser build.** `ToastService.Add`
   (`Services/ShellServices.cs`) called `TaskScheduler.FromCurrentSynchronizationContext()`; Blazor WebAssembly has no
   SynchronizationContext, so it threw and the code after the toast never ran: the options sheet stayed open after
   "Add to order", SEND TO KITCHEN sent the order but left the waiter on the builder with the old cart (double-send
   risk), and Table details skipped its reload after Occupy / Request bill. Now a plain delayed task removes the toast;
   no "Something went wrong" bar appeared in this run. Still: a fresh toast sits over the cart's buttons for its
   3 s (08); top placement on phones would avoid that.
2. **Fixed (368605b)** — 14–20. **The kitchen user had no bottom navigation.** `MainLayout` required *not* `TablesView`
   for the kitchen nav, but the default Kitchen role has it. `Services/ShellModes.For` now decides the start page and
   the nav for both: Kitchen role (with `KitchenOperate`) wins over `TablesView`.
3. **Fixed (368605b)** — 01. **Unstyled app under `dotnet run`.** The shared library keeps its own static-web-asset base
   path, so its CSS is served at `/_content/HotelPOS.Client.Shared/...` in Development *and* after publish (checked:
   the published API has `wwwroot/_content/HotelPOS.Client.Shared/css/`). `/app/_content/...` matches no file and the
   API's authorization fallback policy answers 401. Not an ordering issue in the API's `Program.cs`, so the API is
   unchanged; `HotelPOS.Web/wwwroot/index.html` now links `../_content/...`. **Still open:** the published
   `service-worker-assets.js` lists `app/_content/...` (401) and `service-worker.published.js` builds
   `new Request(asset.url)` relative to `/app/` (giving `/app/app/...`), so the published PWA's offline install fails.
4. **Fixed (368605b)** — 24. **The offline banner appeared only after the next tap.** New `Shared/OfflineBanner.razor`
   re-renders on every connection status change.
5. **Fixed (368605b)** — 23. **No "Order ready" notification reached the waiter.** Not the toast: SignalR binds a
   message with the parameter types of the first handler registered for its name, and the pages subscribe with
   `JsonElement` before the layout's router subscribes with `OrderProgressEvent`, so the router's handler failed its
   cast silently. `WebRealtimeClient` now has one raw handler per event and deserializes per subscriber. The shared
   `NotificationMapper` was not changed.
6. **Open.** Each browser profile uses a licence seat (device identity per browser storage); no self-clean-up of
   never-seen-again browser terminals. The script keeps the identity between runs (`STATE_DIR`); this run signed in
   without hitting the limit.

### Feedback, labels and consistency

7. **Fixed (368605b)** — 02, 09, 21, 22. One set of status words in `Services/StatusLabels`: a submitted order and a ticket
   the kitchen has not accepted are both "Sent to kitchen"; no raw "BillRequested". Table tiles keep the desktop
   legend's words (the map can say "Preparing" while the order says "Sent to kitchen").
8. **Fixed (368605b)** — 21. "Ready" is a green success chip.
9. **Fixed (368605b)** — every screen. Avatar "A(" → "A": initials skip a bracketed role (`ShellModes.Initials`).
10. **Fixed (368605b)** — 02, 09b, 21, 24. Top bar: shorter subtitles that may wrap to two lines; the title keeps one line.
11. **Fixed (368605b)** — 04, 21. Plurals via `TextFormat.Count` ("1 guest", "Order · 1 item"); `Stepper` takes `Unit`.
12. **Fixed (368605b)** — 05. While searching no chip is highlighted; tapping a chip clears the search.
13. **Open.** The kitchen note still needs a second sheet (✎ on the cart line); "Add to order" can wrap; the sheet's
    quantity stepper has only an aria label (07, 07b).
14. **Partly fixed (368605b)** — 09, 22a. CANCEL ORDER is now an outlined danger button set apart from ADD ITEMS. Open:
    OCCUPY and NEW ORDER together with no hint (04); ADD ITEMS / CANCEL ORDER still offered after the bill request
    (the server's `CanModify` decides, same as the desktop).
15. **Open.** Settings shows desktop-only notification kinds to a waiter and gives no "saved" feedback (11).
16. **Open.** The notification panel has no backdrop and its empty state gives no hint (10, 19).
17. **Fixed (368605b)** — 01. Sign-in fields bind on input, so the button enables while typing.

### Layout, contrast and touch targets

18. **Fixed (368605b)** — 08. The cart's lines scroll inside a 24vh area (about 40 % of the phone with the header and
    buttons) and the header ("Order · 4 items · Hide") collapses it to one row.
19. **Fixed (368605b)** — 14, 16, 17. Only the expected next step is solid (ACCEPT, READY, DONE); START and RECALL are
    outlined. A station chip (MAIN / BAR) beside the table code tells one order's tickets apart. "Delayed n" is a red
    chip when n > 0.
20. **Partly fixed (368605b)** — 16, 21, 02. New tokens `--warning-text` (= `--st-preparing`, amber 700) and
    `--success-text` (= `--st-available`) for text in light mode, the bright colours in dark: kitchen note 5.0:1 on white
    (bold, not italic, ✎ prefix), "Bill requested" chip 4.7:1, success/primary chips 5.1:1, prices and the active nav
    label. **Open:** white on the primary green buttons stays 4.39:1 (the brand token is shared with the desktop).
21. **Fixed (368605b)** — 03, 05, 01, 11. `.chip-btn` and the sign-in Waiter/Kitchen toggle are 44 px high; checkboxes
    are 22 px inside a 44 px label. The flow's target scan now reports only the checkboxes (11).
22. **Fixed (368605b)** — 03, 05, 08. Chip rows fade out at the right edge instead of a hard cut.

### Worked well

Offline error state with a plain message and Retry (25); automatic reload after reconnect (24b); kitchen tickets are
large and readable at arm's length with 52 px buttons (16, 17); quick notes and the late (red) ticket border are clear;
dark theme covers every element checked.

## Not captured

- **"Claimed" notice:** none arrived while the flow ran (no cashier claimed the bill).
- A ticket pressed DONE: the flow leaves T06 Ready with the bill requested, so the Completed page (18) lists earlier tickets only.
