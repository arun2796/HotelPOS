# Phase 20 — Enterprise Product Transformation (audit and plan)

Status: Planned, spec for owner review (audit 2026-10-16 on `main` 6acda85) · Depends on: Phases 11–19

Source: the owner's "Enterprise Product Transformation Master Prompt" (2026-10-16). This document is the audit that the
prompt's § 12 requires before any code: every requested capability is classified against the current source, and the
plan below lists the smallest safe change for each gap. Nothing in this document has been implemented yet.

## 1. Goal

Close the gap between the master prompt and what Phases 11–19 already delivered, without rewriting anything: a
role-aware notification system on every client, live tracking that surfaces the events the server already publishes,
role homes (waiter, kitchen, cashier) and a richer manager dashboard, the remaining billing and menu capabilities the
owner approves, and HTTPS by default. Business rules stay on the server; clients differ in presentation only.

## 2. Prerequisites

- Phases 11–19 merged (they are). Phase 10 release build unblocked on the build PC (done 2026-10-16).
- Owner decisions in § 4 before the corresponding step starts.

## 3. Audit result (verified in `src/`; UNVERIFIED means not confirmed from code)

Legend: I = implemented, P = partial, M = missing.

### 3.1 Already delivered by Phases 11–19 (no work in this phase)

| Prompt section | Capability | Status | Where |
|---|---|---|---|
| § 3 design system | Shared tokens, emerald `#078B45` on white, same values desktop and web; dark mode on both | I | `Desktop/Themes/Tokens.*.xaml`, `Client.Shared/wwwroot/css/tokens.css` |
| § 3 | Desktop building blocks (KpiTile, PageHeader, EmptyState, BusyOverlay, Dialog.Frame, Chip.*) | I | `Desktop/Controls`, `Themes/Controls.xaml` |
| § 3 | Web responsive breakpoints (720/900 px), 44 px touch target, safe-area inset, offline banner | I | `Client.Shared/wwwroot/css/app.css`, `ConnectionPill` |
| § 5 tracking | Separate state dimensions: OrderStatus, KitchenOrderStatus, TableStatus, BillStatus, PaymentStatus, PaymentRecordStatus | I | `Contracts/Enums` |
| § 5 | Ticket timers with configurable warn/late minutes (global) | I | settings `KitchenWarnMinutes` / `KitchenLateMinutes`, `KitchenRules.Timing` |
| § 5 | Station routing and hub groups (role, user, device, station) | I | `RestaurantHub`, `HubGroups` |
| § 5 | Reconnect: web re-joins station and refreshes pages; desktop refreshes current page, menu cache, shift bar | I / P | `WebRealtimeClient`, `ShellViewModel.OnReconnected` |
| § 6 billing | Pending-bill queue and claim, itemized totals, GST breakup, discounts with manager approval, split tenders, refunds (partial, approved, audited), void, reopen, reprint log with DUPLICATE, receipt and invoice printing, shifts with expected vs counted, day end, period close | I | `Features/Billing`, `Features/Cash`, `Features/DayEnd`, `Features/Gst`, `Features/Print` |
| § 6 | Idempotency (server middleware, desktop ApiClient, web IdempotentSender with persisted pending key) and row versions | I | `IdempotencyMiddleware`, `RowVersions`, `IdempotentSender`, `DraftStore.PendingKey` |
| § 7 menu | Category/item CRUD, modifiers and add-ons, notes, price and tax per item, station per item, images, sold-out toggle, version sync (`?version=N` → notModified) | I | `Features/Menu`, `GetMenuQuery`, `SetMenuItemAvailability` |
| § 8 kitchen | New/Preparing/Ready columns, station filter, elapsed time, delay colours, accept/start/ready/complete/recall under `kitchen.operate`, resync on reconnect; desktop plays a sound on new ticket | I | `KitchenPage.razor`, `StationPage.razor`, `KitchenDisplayViewModel`, `KitchenSync` |
| § 9 multi-device | Blazor PWA under `/app`, SPA fallback after API routes, HttpOnly refresh cookie scoped to `/api/auth/web`, access token in memory, SignalR `access_token` query auth, web device identity `WEB-WAITER-xxxxxx`, role redirect on `Home.razor`, Android host with SecureStorage and server page | I | `Api/Program.cs`, `WebAuthController`, `DeviceIdentity`, `MauiHostServices` |
| § 10 reliability | Drafts kept locally (both), Sending state, device deactivation forces sign-out on both, online/reconnecting/offline pills | I | `ILocalDraftStore`, `DraftStore`, `StatusBarViewModel`, `ConnectionPill` |
| § 11 dashboard | Manager dashboard: 6 KPI tiles, 7-day bars, top items, payment ring; `my-day` for non-managers; report endpoints incl. `reports/kitchen`, `staff/waiters` | I | `Dashboard/DashboardViewModel`, `api/reports/*` |

### 3.2 Gaps (work for this phase)

| # | Prompt section | Capability | Status | Evidence |
|---|---|---|---|---|
| G1 | § 4 | Web/Android notification bell, unread count, history | M | web has only `Toasts.razor`; desktop has `NoticeCenter` |
| G2 | § 4 | Per-item read/acknowledge and click-to-navigate | M (both) | `NoticeItem` has no target; `MarkAllRead()` only |
| G3 | § 4 | Events published but never surfaced: `BillRequested`, `BillingEvents.Claimed`, `PaymentCompleted`, `DeviceStatusChanged`, delayed ticket | P | hub events exist; no bell/toast handler |
| G4 | § 4 | Sound and vibration with per-type user preference; dedup by key | P | `ISoundPlayer.NewTicket()` only, no settings; `MaxVisible=4` cap, no keyed dedup |
| G5 | § 4 | Server events for printer failure, payment failure, day-end warning | M | only `DocumentPrinted`, `BusinessDayClosed`, `PeriodStatusChanged` |
| G6 | § 4 | Browser push (optional) | M | no `PushManager` in `service-worker*.js` |
| G7 | § 5 | Item-level preparation state | M | `OrderItemStatus` = Draft/Sent/Cancelled only |
| G8 | § 5 | Desktop reconnect refreshes current page only | P | `ShellViewModel.OnReconnected` |
| G9 | § 10 | Web handling of `ConcurrencyConflict` | P | `WaiterApi.OccupyAsync` sends rowVersion, no conflict branch |
| G10 | § 3 | Web loading skeletons, shared error/empty component, `:focus-visible` ring, ARIA; web theme toggle | M / P | "Loading…" text, `.input { outline:none }`, ~9 aria uses, no toggle for `data-theme` |
| G11 | § 11 | Waiter home: my tables, ready items, pending bill requests with counts | P | `TablesPage` shows all tables; no ready-items or bill-request list |
| G12 | § 11 | Kitchen summary: delayed and completed totals | P | column counts only |
| G13 | § 11 | Cashier home: pending bills, awaiting-confirmation payments, shift totals, variance, day-end status | P | pieces spread over `ShiftBarViewModel`, `DayEndView`, `Dashboard` |
| G14 | § 11 | Manager dashboard: kitchen performance, staff, devices, audit panels | P | endpoints exist, dashboard does not use them |
| G15 | § 8 | Web kitchen sound/vibration on new ticket | M | desktop only |
| G16 | § 7 | Waiter favourites and verified search (desktop and phone) | P | no favourite anywhere; search not confirmed |
| G17 | § 6 | Order type dine-in / takeaway / delivery | M | domain is table-based only |
| G18 | § 6 | Split bill by guest or by item | M | only split tenders exist |
| G19 | § 6 | Failed-print recovery queue (server-side retry) | P | `ReserveCopy`/`CompletePrint`; failed copy keeps its number, reprint reserves next |
| G20 | § 9 | HTTPS by default in production | P | certificate script and docs exist; templates and `configure-server.ps1` still write `http://0.0.0.0:5000`; no HSTS |
| G21 | § 3 / § 17 | 125 % scaling and 1600×900 / 1366×768 pass | UNVERIFIED | Phase 17 acceptance box open |

### 3.3 Out of scope (owner decisions already on record)

- Inventory and low-stock alerts: excluded in `docs/00-project-overview.md` § out-of-scope. Kept out unless the owner reverses it (§ 4 D5).
- Real UPI/card gateway: payment methods are configurable data; references are stored. No gateway is invented (prompt § 6).
- E-mail invoice: dropped by the owner 2026-10-15.
- Cashier and manager web clients: prompt § 2 keeps them on WPF "initially"; not in this phase.

## 4. Decisions needed from the owner before the related step

| # | Decision | Recommendation | Affects |
|---|---|---|---|
| D1 | Order types (takeaway, delivery) — adds `OrderType` to `Order`, a migration, order-builder flows on desktop and phone, bill header and reports | Do in a later phase of its own; it touches the domain, three clients and the printed invoice | G17 |
| D2 | Split bill by guest/item — new command, bill numbering (one order → N invoices), GST per invoice | Later phase; financial change needs its own spec and tests | G18 |
| D3 | Item-level preparation state — `OrderItemStatus` gains Preparing/Ready, kitchen UI per item, waiter sees per-item progress | Defer; ticket-level status covers the venue's flow today | G7 |
| D4 | Browser push — needs VAPID keys, a subscription endpoint, and only works over HTTPS; in-app bell stays authoritative | Defer to after G20 ships; optional per prompt | G6 |
| D5 | Inventory — stays out of scope? | Yes, keep out | — |
| D6 | HTTPS by default — installer asks for the certificate, `Urls` gets both 5000 and 5001, HSTS only on the HTTPS endpoint | Yes; required for PWA install and push anyway | G20 |

Steps below assume D1–D4 are deferred and D6 is approved; they are additive and need no migration except step 20.2's
notification-preference settings (stored as settings rows, no schema change).

## 5. Scope

**In:** G1–G5, G8–G16, G19 (visibility only), G20, G21. **Out:** G6, G7, G17, G18 until decided; § 3.3 items.

## 6. Deliverables by step (exact files)

### 20.1 Notification core on both clients (G1, G2, G3, G4)

Shared contract: a `Notification` record `(Id, Kind, Level, Title, Body, OccurredAt, Target)` where `Target` is
`(Type: Order|Bill|Table|Device|Ticket, Id)`. Built client-side from existing hub events; no new server event.

- `src/HotelPOS.Contracts/Realtime/NotificationKind.cs` (new enum: TicketCreated, OrderAccepted, OrderPreparing,
  OrderReady, TicketDelayed, BillRequested, BillClaimed, PaymentCompleted, DeviceOnline, DeviceOffline, ServerNotice,
  PeriodChanged, DayEndWarning, PrintFailed, PaymentFailed).
- Desktop: `Services/Ui/NoticeCenter.cs` — `NoticeItem` gains `Kind`, `Target`, `IsRead`, `Key`; `MarkRead(id)`,
  `Acknowledge(id)`, keyed dedup (same `Key` within 30 s updates instead of adds). `Services/Ui/NotificationRouter.cs`
  (new) subscribes to `IRealtimeClient` events and maps them to notices by role/permission. `NotificationService.cs`
  toast gains an action that calls `AppNavigator` to the target. `Modules/Shell/NoticesPanelView.xaml` shows kind icon,
  severity, time, read state, Acknowledge.
- Web: `Client.Shared/Services/NotificationCenter.cs` (new, same shape, persisted last 50 in `IBrowserStorage`),
  `Client.Shared/Shared/NotificationBell.razor` + `NotificationPanel.razor` (new), wired into `Layout/MainLayout.razor`;
  `WebRealtimeClient` gains the same router. Tap navigates with `NavigationManager`.
- Tests: `tests/HotelPOS.Desktop.Tests` NotificationRouter mapping and dedup; `tests/HotelPOS.Client.Tests` bUnit for bell
  count and panel; rendering test count updated.

### 20.2 Sound, vibration and preferences (G4, G15)

- Desktop `Services/Ui/SoundPlayer.cs`: `Play(NotificationKind)`; `Modules/Admin/SettingsViewModel.cs` + a new
  "Notifications" section on the Settings screen: per-kind toggle for toast, sound (stored under
  `SettingKeys.Notifications.<Kind>` as user preferences in `%LocalAppData%\HotelPOS\<profile>\preferences.json`, not
  server settings — they are per terminal).
- Web: `HotelPOS.Web/wwwroot/js/notify.js` (new: `AudioContext` beep, `navigator.vibrate`), interop in
  `Client.Shared/Services/ClientHostServices`, preferences in browser storage, a Settings sheet on the web shell.
  Android host maps the same interop to `Vibration` in `MauiHostServices.cs`.
- `KitchenPage.razor`: sound + vibration on `KitchenTicketCreated` and on first transition to late.

### 20.3 Server events that do not exist yet (G5)

- `Contracts/Realtime/HubEvents.cs`: `PrintFailed`, `PaymentFailed`, `DayEndDue`.
- `Application/Features/Print/CompletePrint.cs`: publish `PrintFailed` (audience: the device + Manager role) when a
  reserved copy is reported failed.
- `Application/Features/Billing/AddPayment.cs`: publish `PaymentFailed` to the cashier's device on a rejected payment
  (validation or concurrency), never on success before commit.
- `Application/Features/DayEnd`: a hosted check (`Api/Hosting/DayEndReminder.cs`, new) that publishes `DayEndDue` to
  Manager/Admin at a configurable time (`SettingKeys.DayEndReminderTime`, default 23:00) when the day is still open.
- Tests: Api tests for each event's audience; `authorization-matrix.txt` unchanged (no new endpoints).

### 20.4 Reconnect and conflict hardening (G8, G9)

- Desktop `ShellViewModel.OnReconnected`: also refresh `NoticeCenter` from `api/system/notices` (existing) and the
  billing queue if open; mark the gap in the bell ("Reconnected — refreshed at HH:mm").
- Web `WaiterApi.cs`: map 409 `CONCURRENCY_CONFLICT` to a typed result; `TableDetailsPage.razor` and
  `OrderBuilderPage.razor` show "Changed on another device — reloaded" and refetch.
- Tests: `WebFlowTests` conflict path; desktop reconnect test.

### 20.5 Web polish (G10)

- `app.css`: `:focus-visible` ring using `--focus` token, remove `outline:none` from `.input`; `.skeleton` shimmer class.
- `Client.Shared/Shared/Skeleton.razor`, `ErrorState.razor`, `EmptyState.razor` (new); replace "Loading…" on
  `KitchenPage`, `TablesPage`, `MyOrdersPage`, `OrderBuilderPage`.
- Theme toggle in `MainLayout.razor` writing `data-theme`, persisted in browser storage.
- ARIA pass: roles on nav, lists, live region for toasts and the bell count.

### 20.6 Role homes and dashboard panels (G11–G14)

- Waiter `Client.Shared/Pages/Waiter/HomePage.razor` (new; `/home` becomes the waiter landing): My tables (tables whose
  open order's waiter is me, from existing `OrderSummaryDto.WaiterId`), Ready items, Bill requests — counts and lists
  from existing endpoints `api/tables`, `api/orders/mine`, no new API.
- Kitchen `KitchenPage.razor` header: Delayed and Completed-today counts (`CompletedPage` data already loaded).
- Cashier desktop `Modules/Cash/CashierHomeViewModel.cs` + view (new, shown as Dashboard for Cashier role): pending
  bills (`api/bills?status=pending`), my open shift totals and expected cash (`ShiftBarViewModel` source), day-end
  status, print failures from 20.3. Registered in `ModuleRegistry`.
- Manager `DashboardViewModel`: three new panels using existing endpoints — kitchen performance (`reports/kitchen`:
  avg prep time, late %), staff today (`reports/staff/waiters`), devices online/offline (`api/admin/devices`) with a
  link to the audit log. Chart via existing `ChartGeometry`.
- Tests: VM tests for each; rendering test screenshots.

### 20.7 Menu speed (G16)

- Favourites are per terminal/profile (no server change): desktop `OrderBuilderViewModel` + web `OrderBuilderPage`
  get a "★ Favourites" pseudo-category sourced from local preferences (most-ordered on this device, pin/unpin).
- Verify and, if missing, add search-as-you-type over name and code on both builders.

### 20.8 Print failure visibility (G19)

- Desktop `Modules/Printing/PrintLogView`: filter "Failed" and a Retry action that calls the existing
  `ReserveCopy` (next copy number, DUPLICATE marking preserved). No server queue.

### 20.9 HTTPS by default (G20, D6)

- `Api/appsettings.Production.template.json`: `Kestrel:Endpoints:Http` 5000 and `Https` 5001 with certificate path
  placeholders; `scripts/configure-server.ps1`: `-CertificatePath/-CertificatePassword` (or call
  `new-lan-certificate.ps1` when absent), firewall rule for 5001; `installer/server.iss`: certificate page.
- `Api/Program.cs`: `UseHsts()` only when an HTTPS endpoint is configured; no `UseHttpsRedirection` (desktop terminals
  on plain LAN keep working on 5000).
- `docs/ops/01-installation.md`, `02-lan-and-firewall.md`, `09-android-app.md` updated.

### 20.10 Verification (G21 and prompt § 13)

- Full suite; rendering test at 100 % and 125 % (`ScreenRenderingTests` DPI parameter) at 1920×1080, 1600×900,
  1366×768; `HOTELPOS_E2E` resilience test; device demo per § 11.

## 7. Data model

No migration. New user preferences live in the per-terminal preferences file (desktop) or browser storage (web).
`SettingKeys.DayEndReminderTime` is a settings row seeded by `DbSeeder` (settings table already exists).

## 8. API endpoints

None new. Existing endpoints reused: `api/tables`, `api/orders/mine`, `api/bills`, `api/reports/kitchen`,
`api/reports/staff/waiters`, `api/admin/devices`, `api/system/notices`, `api/print/*`.

## 9. Real-time events

New: `PrintFailed`, `PaymentFailed`, `DayEndDue` (§ 20.3). All others reuse the existing catalogue in
`docs/04-realtime-events.md`, which is updated with audience and client handling per kind.

## 10. Tests

Desktop VM tests (router, dedup, cashier home, dashboard panels), Client bUnit tests (bell, panel, skeletons, conflict
path), Api tests (three new events' audiences, day-end reminder), rendering test counts, authorization matrix unchanged.

## 11. Manual demo script

1. Phone waiter orders; kitchen tablet gets sound + vibration; waiter bell shows "Accepted", tap opens the order.
2. Ticket passes the late threshold: kitchen header Delayed = 1, manager bell gets "Ticket #N delayed".
3. Waiter requests bill: cashier toast "Bill requested T01", click opens the queue; claim → waiter bell "Claimed by Asha".
4. Payment: cashier sees Paid only after the server event; a rejected (stale rowVersion) payment shows "Payment failed".
5. Pull the kitchen tablet's Wi-Fi: Offline pill; restore: Reconnected, tickets resynced, bell shows the gap.
6. Admin deactivates a device: manager bell "KITCHEN-01 offline"; the device signs out.
7. 23:00 with the day open: manager and admin get "Day end due".
8. Print to an unplugged printer: cashier bell "Print failed", Print log → Retry prints DUPLICATE copy 2.
9. Web: Tab through the order builder (focus rings visible); toggle dark theme; skeletons on first load.
10. Server on HTTPS 5001: phone installs the PWA; desktop terminals still connect on 5000.

## 12. Acceptance criteria

- [ ] Every event in § 3.2 G3 is visible in the right role's bell on desktop and web; none leaks to other roles.
- [ ] Bell items navigate to their target; acknowledge persists across reload (web) and session (desktop).
- [ ] Sound/vibration per kind can be switched off per terminal.
- [ ] Waiter home, kitchen counts, cashier home, manager panels show live data from existing endpoints only.
- [ ] Web conflict path reloads instead of failing silently; desktop reconnect refreshes notices.
- [ ] HTTPS endpoint configured by the installer; HTTP terminals unaffected.
- [ ] Full suite green; rendering tests at 100 % and 125 %; device demo § 11 run.

## 13. Risks and notes

- Notification volume on busy nights: keyed dedup and the 50-item cap keep the bell usable; sounds default on only for
  kitchen `TicketCreated`, cashier `BillRequested`, and `PrintFailed`.
- Per-terminal preferences are deliberately not server settings: the same user on a kitchen tablet and a phone wants
  different sounds.
- `DayEndDue` is a reminder, not a lock; Phase 13 rules unchanged.
- HSTS is scoped to the HTTPS endpoint so a mixed LAN (desktop on 5000, phones on 5001) keeps working.

## 14. Changes during implementation

(filled while building)

### 20.5 Web polish (G10)

- `app.css`/`tokens.css`: new `--focus` token (light and dark) and a global `:focus-visible` ring; `outline:none` removed from `.input`. `.skeleton` shimmer (disabled under `prefers-reduced-motion`), `.sr-only`.
- New `Client.Shared/Shared/Skeleton.razor` (Line/Card variants, `role=status aria-busy`), `ErrorState.razor` (`role=alert`, optional Retry callback), `EmptyState.razor`, `ThemeToggle.razor` (system, light, dark; persisted under `hotelpos.theme` through `IBrowserStorage`, applied by `hotelposTheme.set` in `js/theme.js`, referenced from the Web and Android `index.html`; the Android copy lives in `HotelPOS.Android/wwwroot/js`).
- Markup-only swaps of "Loading…" and ad-hoc retry banners on KitchenPage, CompletedPage, TablesPage, MyOrdersPage, OrderBuilderPage, TableDetailsPage.
- `MainLayout`: theme toggle in the top bar, `aria-label` on both `nav` blocks, `aria-current="page"` on the active link, decorative icons `aria-hidden`, "Home" link (`/home`) for waiters. `Toasts` live region is now always in the DOM so announcements are reliable.
- Tests: `WebPolishTests` (6 bUnit tests); Client.Tests 33 passing.
- Not done: bell-count live region and `HomePage` skeleton (NotificationBell and HomePage come from 20.1/20.6 on other branches; re-apply the same swaps when merged); the `/home` link is dead until 20.6 lands.
