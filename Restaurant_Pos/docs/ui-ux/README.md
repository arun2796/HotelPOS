# HotelPOS UI/UX gallery

Screenshots of the real application against a running Development API (seeded demo data). The desktop cashier and
manager folders were regenerated on 2026-10-17 on branch `phase-21-role-ui-ux` (API on port 5100) after the Phase 21
redesign; the rest dates from 2026-10-16 on `main` after Phase 20 unless its README says otherwise. Every image comes
from the actual flow with real data; nothing is mocked. Each folder has its
own README with a table per image and the UI/UX issues noticed while driving the app.

| Folder | Client | Role and flow | Images |
|---|---|---|---|
| [web/waiter](web/README.md) | Browser PWA, phone 390×844 | **Phase 21.5 (2026-10-17, branch phase-21-role-ui-ux, API :5100):** sign-in, home KPIs, tables, pick-a-table sheet, order builder (search, favourites, options sheet, cart bar, review sheet), order details (Details / Items / Timeline), my orders (Active / Completed), bell, More sheet, settings, dark theme, served, bill request, offline | 28 |
| [web/kitchen](web/README.md) | Browser PWA, tablet 1024×768 | **Phase 21.5:** sign-in, board with KPI strip (New / Preparing / Ready), ticket detail pane, Delayed filter, station, accept → start → mark ready, completed, bell, dark theme | 11 |
| [desktop/cashier](desktop/README.md) | WPF 1366×800, Phase 21 | Shift open, the three-pane cashier workspace (queue, bill, payment; also with the collapsed sidebar), discount, cash via F2, split payment (cash + card), inline Paid state, print attempts, Paid chip, print log, closed bills, pay in/out, X report, close shift, bell, notification preferences | 19 |
| [desktop/manager](desktop/README.md) | WPF 1366×800, Phase 21 | Navy grouped sidebar, dashboard (KPI cards, order-status donut, active orders table, table status, recent activity; light, dark and collapsed), Orders page, Sections & Tables, Menu tabs, item editor, audit, reports, GST, day end, printers, palette, as `manager1` | 16 |
| [desktop/admin](desktop/README.md) | WPF 1366×800, **pre-Phase 21** | Admin-only screens (users, roles, devices, settings, licence, server) from 2026-10-16; the admin password was not available on 2026-10-17, so they still show the old shell | 7 |
| [desktop/functions](desktop/README.md) | WPF 1366×800, **pre-Phase 21** | Before / dialog / after pairs for every function from 2026-10-16 (users, roles, sections and tables, categories and items, sold-out, settings, printers, exports, audit filter, day-end precheck, cancel line, customer, refund); the legacy bill-page shots were removed | 71 |

## Issues found

- Web (Phase 21.5 run, [web/README.md](web/README.md)): 8 issues against the mock-ups. One high — a long table timer widened the
  Tables page past the phone and broke taps (fixed in the run, `src/HotelPOS.Client.Shared/wwwroot/css/waiter.css`); open: status word clipped in table cards, "+ options"
  hint clipped, toasts over the cart bar, top-bar title truncation, several 36–44 px targets under the 48 px rule, "Ready n h"
  reads as time since ordered, licence seat per browser profile. The 22 Phase 20 issues: 15 fixed then, the rest superseded by the restyle or carried here.
- Desktop: the Phase 21 pass (2026-10-17) found 8 issues, listed with severity in [desktop/README.md](desktop/README.md);
  the clipped right-hand grid columns on Orders and the dashboard were fixed in the same pass. The Phase 20 issues
  still open are listed there as carry-overs (the full 41-item list is in git history).

## Regenerating

- Web: `docs/ui-ux/tools/web-flow.mjs` (Playwright; header explains the scratch-folder setup). Writes into
  `web/waiter` and `web/kitchen`.
- Desktop: opt-in test `tests/HotelPOS.Desktop.Tests/Rendering/LiveFlowScreenshots.cs`, run alone with
  `HOTELPOS_LIVE_SCREENS=1`, `HOTELPOS_LIVE_API=<api url>` (default `http://localhost:5000`), `HOTELPOS_LIVE_SCREENS_ROLES`
  (plus `HOTELPOS_ADMIN_PASSWORD` for the admin screens) against a running API, built to an OutDir inside the repo:
  `dotnet test tests/HotelPOS.Desktop.Tests -c Debug -p:OutDir=<repo>/tests/HotelPOS.Desktop.Tests/bin/p21live/ --filter "FullyQualifiedName~LiveFlowScreenshots"`.

Both runs create demo data (orders on T02/T05, a shift, a test user, section, table, category, item and customer) and
register terminals; deactivate the `WEB-*` / `SCREENS-*` devices afterwards so they do not use licence seats.
