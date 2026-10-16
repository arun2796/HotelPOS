# HotelPOS UI/UX gallery

Screenshots of the real application, captured 2026-10-16 against a running Development API (seeded demo data) on
`main` after Phase 20. Every image comes from the actual flow with real data; nothing is mocked. Each folder has its
own README with a table per image and the UI/UX issues noticed while driving the app.

| Folder | Client | Role and flow | Images |
|---|---|---|---|
| [web/waiter](web/README.md) | Browser PWA, phone 390×844 | Sign-in, home, tables, order builder (search, favourites, options, note), send, my orders, bell, settings, dark theme, bill request, offline | 26 |
| [web/kitchen](web/README.md) | Browser PWA, tablet 1024×768 | Sign-in, board (New / Preparing / Ready), station, accept → start → ready, completed, bell, dark theme | 11 |
| [desktop/cashier](desktop/README.md) | WPF 1366×800 | Shift open, billing queue, claim, discount, split payment (cash + card), paid, print attempts, print log, closed bills, pay in/out, X report, close shift, bell, notification preferences | 16 |
| [desktop/manager](desktop/README.md) | WPF 1366×800 | Manager dashboard and management screens as `manager1` | 13 |
| [desktop/admin](desktop/README.md) | WPF 1366×800 | Admin dashboard with panels, tables, menu, users, roles, devices, audit, reports, GST, day end, settings (Notifications), printers, licence, server, palette, dark theme | 20 |
| [desktop/functions](desktop/README.md) | WPF 1366×800 | Before / dialog / after pairs for every function: users, roles, sections and tables, categories and items, sold-out, settings, printers, exports, audit filter, day-end precheck, cancel line, customer, over-limit discount, refund, reopen (all with real approvals) | 83 |

## Issues found

- Web: 22 issues in [web/README.md](web/README.md). The most serious: toasts throw in WebAssembly and leave pages stuck
  (options sheet, order builder after send, table page after bill request); the kitchen role has no bottom navigation;
  the offline banner lags one tap; no "ready" notice reaches the waiter's bell.
- Desktop: 41 issues in [desktop/README.md](desktop/README.md). The most serious: toasts cover modal buttons and survive
  navigation; the cashier is notified about its own actions; bill lines hide chosen modifiers; print-failure notice uses
  the bill id; lists do not scroll to the changed row; several clipped columns and truncations.

## Regenerating

- Web: `docs/ui-ux/tools/web-flow.mjs` (Playwright; header explains the scratch-folder setup). Writes into
  `web/waiter` and `web/kitchen`.
- Desktop: opt-in test `tests/HotelPOS.Desktop.Tests/Rendering/LiveFlowScreenshots.cs`, run alone with
  `HOTELPOS_LIVE_SCREENS=1` (plus `HOTELPOS_ADMIN_PASSWORD` for the admin screens) against a running API:
  `dotnet test tests/HotelPOS.Desktop.Tests -c Debug --filter "FullyQualifiedName~LiveFlowScreenshots"`.

Both runs create demo data (orders on T02/T05, a shift, a test user, section, table, category, item and customer) and
register terminals; deactivate the `WEB-*` / `SCREENS-*` devices afterwards so they do not use licence seats.
