# Phase 20 — Enterprise Product Transformation (audit and plan)

Status: In progress — code complete 2026-10-16 (steps 20.1–20.9 merged on `main`; audit on 6acda85); open: § 11 device demo, 125 % pass · Depends on: Phases 11–19

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

- [x] Every event in § 3.2 G3 is visible in the right role's bell on desktop and web; none leaks to other roles
      (router/mapper tests on both clients, 2026-10-16; cross-device check is demo § 11 step 1–3).
- [x] Bell items navigate to their target; acknowledge persists across reload (web) and session (desktop)
      (web: Bill/Device targets mark read without navigating — no page for them; desktop: all targets).
- [x] Sound/vibration per kind can be switched off per terminal (desktop dialog for every role + Settings; web /settings).
- [x] Waiter home, kitchen counts, cashier home, manager panels show live data from existing endpoints only (no API change).
- [x] Web conflict path reloads instead of failing silently; desktop reconnect refreshes the current page and adds a
      "Reconnected" notice (there is no stored-notice endpoint to reload; push-only by design).
- [ ] HTTPS endpoint configured by the installer; HTTP terminals unaffected (code + HSTS tests done; installer run on a
      clean VM still open — Phase 10 § 15 smoke test).
- [ ] Full suite green (2026-10-16: Domain 365, Client 66, Api 197, Desktop 296 + 1 skipped, Application 253);
      rendering at 125 % and device demo § 11 still open.

## 13. Risks and notes

- Notification volume on busy nights: keyed dedup and the 50-item cap keep the bell usable; sounds default on only for
  kitchen `TicketCreated`, cashier `BillRequested`, and `PrintFailed`.
- Per-terminal preferences are deliberately not server settings: the same user on a kitchen tablet and a phone wants
  different sounds.
- `DayEndDue` is a reminder, not a lock; Phase 13 rules unchanged.
- HSTS is scoped to the HTTPS endpoint so a mixed LAN (desktop on 5000, phones on 5001) keeps working.

## 14. Changes during implementation

### 20.1 Notification core on both clients (G1–G4) — done

- **Shared mapping in Contracts.** `Contracts/Realtime/NotificationKind.cs` holds the `NotificationKind` enum (the 15
  kinds), `NotificationTargetType`/`NotificationTarget` (type + string id), `NotificationDraft`, `NotificationAudience`
  and `NotificationMapper`. The mapper decides which role sees which hub event and what it says, so desktop and browser
  cannot drift. Roles: waiter sees progress of its own orders only; kitchen sees new tickets; cashier/manager/admin see
  bill requested, claimed and payment; manager/admin see device status; everyone sees server notices and GST period
  changes. No new event, endpoint or permission.
- **Desktop.** `NoticeItem` is now observable with `Id`, `Kind`, `Target`, `Key`, `IsRead`, `IsAcknowledged`, glyph and
  level text. `NoticeCenter` gained `MarkRead`, `Acknowledge`, per-item unread count and keyed dedup (same key within
  30 s updates the entry; `Add` returns false for a repeat so no second toast). New `Services/Ui/NotificationRouter.cs`
  (`INotificationRouter`) replaces `ReadyNotifier` and `PeriodStatusNotices` (both deleted) and also takes the
  `ServerNotice` broadcast from `AppNavigator` (which keeps only the `DEVICE_DISABLED` sign-out). Toasts for entries with
  a target carry an "Open" button (`INotificationService.Notify`) that navigates through `INavigationService`. The
  shell starts the router for every role (it was waiter-only) and stops it on dispose. The notices panel in
  `Shell/ShellView.xaml` (there is no `Modules/Shell` folder) shows kind icon, severity, time, read state, an
  Acknowledge button and Mark all read; tapping an entry marks it read and opens its screen. Opening the panel no longer
  marks everything read.
- **Web/Android.** `Services/NotificationCenter.cs` (same shape, last 50 in `IBrowserStorage`, cleared on sign-out),
  `Services/NotificationRouter.cs`, `Shared/NotificationBell.razor`, `Shared/NotificationPanel.razor`, wired into
  `Layout/MainLayout.razor` next to `ConnectionPill`; `ToastService` gained a Warning kind. Tap navigates with
  `NavigationManager` to `/tables/{id}`, `/my-orders` or `/kitchen`; bill and device entries have no browser page.
- **Tests.** `tests/HotelPOS.Desktop.Tests/NotificationRouterTests.cs` (role mapping, no leaks, dedup, 50 cap, toast
  navigation, read/acknowledge; the period-notice test moved here from `GstViewModelTests`),
  `tests/HotelPOS.Client.Tests/NotificationTests.cs` (bUnit: bell count, panel list order, tap marks read and navigates,
  acknowledge, persistence across reload, dedup and cap, target-to-URL, shared mapper). The rendering test registers a
  substitute `INotificationRouter` instead of `IReadyNotifier`; no screen was added, so the screen count is unchanged.
- **Left for later steps.** Sound and vibration (20.2); `TicketDelayed`, `DayEndWarning`, `PrintFailed`, `PaymentFailed`
  mappings arrive with their server events (20.3).

### 20.3 Server events (G5)

- Added `PrintFailed`, `PaymentFailed`, `DayEndDue` (`HubEvents`, payload records beside `RealtimeEvent`).
- `CompletePrint` queues `PrintFailed` after commit when a copy is reported failed: audience is the reporting device
  (or the user when no device is on the token) plus the Admin and Manager roles (same managers as `DocumentPrinted`).
- `AddPayment` publishes `PaymentFailed` to the paying device only when the handler rejects the payment with a
  validation or concurrency error. It is published directly because the transaction never commits on those paths.
  Failures of the request-body validators (for example amount <= 0) run before the handler and do not publish; the
  client already shows those inline. Refunds are not covered (spec names AddPayment only).
- `DayEndReminder` (`Application/Features/DayEnd`) decides; `Api/Hosting/DayEndReminderService` ticks once a minute.
  Setting `DayEndReminderTime` (HH:mm, default 23:00, seeded for existing databases too). Fires once per business day,
  not when that day is closed. Measured on the business-day clock, so a time such as 01:00 means after midnight of the
  same business day.

### 20.6 Role homes and dashboard panels (G11-G14)

- Waiter home: `Pages/Waiter/HomePage.razor` at `/home`; `Home.razor` now sends waiters there (Kitchen still goes to `/kitchen`). It uses `api/orders/active` filtered to the signed-in waiter (`OrderSummaryDto.WaiterId`), not `api/tables`: the order summary already carries table code, status and totals, and "ready" and "bill requested" are order statuses, so a second call would add nothing. Ready is counted per order (the API has no item-level preparation state; that is G7/20.4). The "Home" nav link needs `MainLayout.razor`, which belongs to 20.1, so `/tables` and `/my-orders` stay the nav links for now.
- Kitchen (browser): header line with Delayed (open tickets at or past the late threshold via `KitchenRules.Timing`) and Completed today (extra `CompletedAsync` call on each refresh; a failure leaves the count at a dash).
- Cashier home: `Modules/Cash/CashierHomeViewModel` + view, new module `cashier-home` (title "Dashboard", role Cashier, phase 20). Cashier is removed from the old Dashboard module's roles, and `HomeFor(Cashier)` is now the cashier home (was Billing). Tiles use the billing queue (`GetPendingAsync`), `api/shifts/current` plus the X report for takings and expected cash, and the business-day events for the day state.
- Left out, on purpose: (1) "Print failures" tile: no endpoint lists failed print copies for a terminal (`api/print/{type}/{id}/history` is per document); it comes with the 20.8 failed-print queue. (2) Day-end status from the precheck: `api/day-end/precheck` is Admin/Manager only (authorization matrix), so the cashier tile follows `BusinessDayClosed`/`BusinessDayReopened` and shows "Open" until told otherwise. (3) Awaiting-confirmation payments and shift variance: no existing cashier-readable source (variance exists only after close).
- Manager dashboard: kitchen performance (`reports/kitchen`, ticket-weighted average prep and late %), staff today (`reports/staff/waiters`, top six by net sales), terminals (`api/admin/devices`) with an "Open audit log" command. The devices endpoint is administrator-only, so the Terminals list is shown to Admin only; a Manager sees an explanatory empty state and the audit button. Compact tables rather than a chart (rows with a few numbers read better than another chart; `ChartGeometry` is unchanged). `DashboardViewModel` gained an `IDevicesApi` constructor parameter and refreshes on `DeviceStatusChanged`.
- Tests: `Cash/CashierHomeViewModelTests`, `Reports/DashboardPanelTests`, `Client.Tests/WaiterHomePageTests` (bUnit); `ScreenRenderingTests` renders `74-cashier-home` (count 73 to 74) and the demo reports API now returns kitchen and staff rows for the admin dashboard; `ShellTests` expects the cashier home for `HomeFor(Cashier)`.

### 20.9 HTTPS by default (G20, D6)

- Template: `Kestrel:Endpoints` Http 5000 + Https 5001 with `<set by installer>` certificate placeholders; `Urls` is set to empty because a `Urls` value (the base appsettings.json has one) overrides Kestrel endpoints.
- `configure-server.ps1`: `-HttpsPort`, `-CertificatePath`, `-CertificatePassword` (also handover keys). Without a certificate it keeps the one in the existing settings, else calls `new-lan-certificate.ps1` (host name + non-loopback IPv4 addresses, random PFX password). Firewall opens 5000 and 5001. Health check stays on http://localhost:5000.
- `installer/server.iss`: certificate page (both optional; a given file must exist and have a password). Not compiled here (no publish folder).
- `Program.cs`: `UseHsts()` only outside Development/Testing and only when `Kestrel:Endpoints:Https` exists; no `UseHttpsRedirection`. Test hook `Hsts:Enabled=true` lets `HstsTests` exercise it in the Testing environment (3 tests).
- Docs: ops 01, 02, 05, 09 updated. Root CA distribution unchanged.

### 20.5 Web polish (G10)

- `app.css`/`tokens.css`: new `--focus` token (light and dark) and a global `:focus-visible` ring; `outline:none` removed from `.input`. `.skeleton` shimmer (disabled under `prefers-reduced-motion`), `.sr-only`.
- New `Client.Shared/Shared/Skeleton.razor` (Line/Card variants, `role=status aria-busy`), `ErrorState.razor` (`role=alert`, optional Retry callback), `EmptyState.razor`, `ThemeToggle.razor` (system, light, dark; persisted under `hotelpos.theme` through `IBrowserStorage`, applied by `hotelposTheme.set` in `js/theme.js`, referenced from the Web and Android `index.html`; the Android copy lives in `HotelPOS.Android/wwwroot/js`).
- Markup-only swaps of "Loading…" and ad-hoc retry banners on KitchenPage, CompletedPage, TablesPage, MyOrdersPage, OrderBuilderPage, TableDetailsPage.
- `MainLayout`: theme toggle in the top bar, `aria-label` on both `nav` blocks, `aria-current="page"` on the active link, decorative icons `aria-hidden`, "Home" link (`/home`) for waiters. `Toasts` live region is now always in the DOM so announcements are reliable.
- Tests: `WebPolishTests` (6 bUnit tests); Client.Tests 33 passing.
- Not done: bell-count live region and `HomePage` skeleton (NotificationBell and HomePage come from 20.1/20.6 on other branches; re-apply the same swaps when merged); the `/home` link is dead until 20.6 lands.
### 20.4 Reconnect and conflict hardening (G8, G9)

- Desktop: the reconnect handling moved from `ShellViewModel.OnReconnected` into `Shell/ReconnectCoordinator`, which the shell owns. On `Reconnected` it refreshes the current page when it is `IRefreshable` (this covers the billing queue, which is only ever the current page: pages are not cached), reloads the restaurant name, and leaves one Info bell entry "Reconnected — refreshed at HH:mm". A repeat replaces the earlier entry instead of stacking. `MenuCache` and `ShiftBar` keep their own `Reconnected` subscriptions, unchanged.
- No endpoint returns stored server notices (`ServerNotice` is push-only; the only notice endpoint is the admin POST), so notices missed during an outage cannot be reloaded. The bell entry marks the gap instead.
- Web: `ApiResult<T>.IsConflict` is true for the `CONCURRENCY_CONFLICT` error code (HTTP 409); `WaiterApi.ConflictMessage` holds "Changed on another device — reloaded". `TableDetailsPage` shows it as an error toast and re-fetches the table and order. `OrderBuilderPage` shows it, re-reads the order, table and menu, and keeps the cart, the saved draft and the pending idempotency key. A conflict is a definite answer, so `IdempotentSender` does not retry it.
- Tests: `ReconnectCoordinatorTests` (desktop, fake realtime client, 2) and `ConflictHandlingTests` (Client.Tests, stub HTTP handler, 3). No page-level bUnit test: the pages need a signed-in `WebRealtimeClient`, which opens a real SignalR connection.
### 20.2 Sound, vibration and preferences (G4, G15)

- Contracts: `NotificationMapper` gained `PrintFailed`, `PaymentFailed`, `DayEndDue` (maps to the existing `DayEndWarning` kind, Manager/Admin only) and the client-raised `TicketDelayed` (kitchen and managers); `PrintFailed`/`PaymentFailed` are shown to whoever the server addressed them to. New `NotificationPreference(Toast, Sound)` and `NotificationPreferenceDefaults` (role-based defaults and labels, spec section 13) shared by desktop and web.
- Desktop: `ISoundPlayer` is now `Play(NotificationKind)` (the old `NewTicket()` is gone). `NotificationPreferences` reads/writes per-kind choices in `preferences.json` through two new `IUserPreferences` methods (stored by kind name, not as `SettingKeys.Notifications.<Kind>`, because these are not server settings). `NotificationRouter` plays the sound and shows the toast only when the preference allows (the bell entry is always kept), subscribes to the three new events and exposes `TicketDelayed(...)`. `KitchenDisplayViewModel` no longer plays its own new-ticket sound (the router does, so there is no double sound); it takes `INotificationRouter` and raises `TicketDelayed` when a card first turns late on a timer tick (cards already late on load stay quiet). Settings screen: "Notifications on this terminal" section under the settings list, two checkboxes per kind, saved at once.
- Web/Android: `wwwroot/js/notify.js` (`hotelposNotify.signal`: AudioContext beep + `navigator.vibrate`), `IAlertSignal` in Client.Shared (a `SilentAlertSignal` is the fallback), `JsAlertSignal` (browser), `MauiAlertSignal` (Vibration + `ToneGenerator`, `VIBRATE` permission added). `NotificationPreferences` (Client.Shared) keeps choices in `IBrowserStorage` under `notify.prefs.v1`. `NotificationRouter` honours them, handles the three new events and exposes `TicketDelayed`. New page `Pages/Settings.razor` at `settings`. `KitchenPage` calls `TicketDelayed` the first time a ticket turns late (silent baseline on first load); new-ticket beeps come from the router's existing `KitchenTicketCreated` handling.
- Left for merge: the nav link to `settings` (MainLayout is owned by 20.5); until then the page is reachable by URL. The Android beep uses `ToneGenerator`, not the WebView JS, and has not been run on a device. The desktop Settings screen is Admin-only (permission), so a non-admin terminal cannot change its notification choices yet. On the desktop the new-ticket sound now ignores the kitchen display's station filter (it follows the role).
- Tests: `Desktop.Tests/NotificationSoundTests` (mapper, defaults, router sound/toast, settings VM), `KitchenDisplayViewModelTests` (late transition), `Client.Tests/NotificationPreferenceTests` (preferences, router calls the signal, Settings page).

### 20.2/20.5 follow-ups

- **Web nav.** `MainLayout` bottom nav: waiter Home, Tables, My orders, Settings; kitchen Kitchen, Completed, Station, Settings. The Settings link (`/settings`) uses the same `aria-current="page"` logic as the others. The Home link from 20.5 was already present and is unchanged.
- **Bell accessibility.** `NotificationBell` has a hidden (`.sr-only`) `role="status" aria-live="polite"` region that reads "N unread notification(s)" and is empty at zero. The button already had `aria-label` and `aria-expanded`. `NotificationPanel` is `role="dialog"` labelled by its heading (`aria-labelledby`); the close button reads "Close notifications" with the glyph hidden.
- **Waiter home.** `HomePage` uses `Skeleton` while loading, `ErrorState` (with Retry) for a failed load and `EmptyState` for the three empty hints. Markup only.
- **Desktop preferences for every role.** The Settings screen is Admin only, so the sidebar user block has a new bell icon "Notifications…" (`OpenNotificationPreferencesCommand`) that opens `NotificationPreferencesViewModel` / `NotificationPreferencesView` in a `Dialog.Frame` overlay over the page (Esc or Done closes it). The per-kind Pop-up/Sound list is now one reusable control, `Modules/Common/NotificationPreferencesSection`, used by both the dialog and `SettingsView`; both bind to `NotificationPreferenceItemViewModel` rows over the same `NotificationPreferences` store. No API, permission or matrix change.
- **Tests.** `Client.Tests/AccessibilityFollowUpTests` (nav links for waiter and kitchen incl. aria-current, bell live region/aria/dialog label, HomePage skeleton then empty states); `Desktop.Tests/NotificationSoundTests.PreferencesDialog_...` (every kind, toggle persists to the shared store, Close); `ScreenRenderingTests` renders `75-notification-preferences` (count 74 to 75) and checks the shell command works for a Kitchen user.
- **Not done.** The dialog is not added to the Ctrl+K palette.
### 20.8 Print failure visibility (G19)

- New desktop screen `Modules/Printing/PrintLogView` + `PrintLogViewModel`, module `print-log` ("Print Log", Billing group, Admin/Manager/Cashier, `BillingOperate`). Filter All / Failed, and a Retry per failed copy that calls `IDocumentPrinter.PrintAsync` (ReserveCopy, render, CompletePrint), so the retry takes the next copy number and prints DUPLICATE; the failed copy keeps its number. The outcome shows inline (`IPrintQueue.Completed`, new event with `PrintOutcome`) and the list refreshes. A copy the server still lists as Reserved but this terminal knows failed counts as Failed (the result report is fire-and-forget).
- No endpoint was added, and none exists that lists print copies across documents (`api/print/{type}/{id}/history` is per document). So the screen, the cashier tile and the filter are client-side over this terminal's failed jobs in `PrintQueue`: it reads each such document's history and keeps this terminal's copies (`DeviceName`). `PrintQueueOptions.KeepFailedFor` went from 10 minutes to 8 hours so a failure is still there when the cashier looks. The list is in memory, so it is empty after an app restart, and a failure on another terminal is not shown on a manager's terminal (the manager gets the bell notice, which opens an empty Failed list).
- Cashier home: "Print failures" tile (between Expected cash and Business day) from `IPrintQueue.FailedCount`, live on queue changes; it opens the print log with the Failed filter. The strip is now 5 columns (`CashierHomeViewModel` takes `IPrintQueue` and `IUiDispatcher`).
- Bell: `NotificationTargetType.Print` added (appended) and `NotificationMapper.PrintFailed` now carries it (Id = document id); the desktop router opens the print log on Failed for Admin/Manager/Cashier and does nothing for other roles. The browser router maps it to no page.
- Tests: `Printing/PrintLogViewModelTests` (5), cashier tile test, `NotificationRouterTests` print target (3 cases), `ScreenRenderingTests` renders `76-print-log` (count 74 to 75).
### 20.7 Menu speed (G16)

- Desktop: `IUserPreferences` gained `PinnedItemIds` (pin order) and `ItemOrderCounts` (item id to times sent), stored in `preferences.json`. `OrderBuilderViewModel` takes `IUserPreferences`; `BuildMenu` inserts a pseudo-category `FavouritesCategoryId` (-1, "★ Favourites") first only when it has items, and selects it when nothing was selected. Favourites = pinned items on the menu, then the top 8 (`MostOrderedCount`) by count (ties by name). `RecordOrdered` runs only on a successful submit (not for a saved draft or a rejected/unconfirmed send), +1 per distinct item, and keeps the 200 highest counters (`MaxTrackedItems`). `TogglePinCommand` is on each tile (`MenuTileViewModel.IsPinned`): a 40 x 40 star in the corner of the picture, inside the existing 172 px card; sold-out tiles cannot be pinned (the whole card is disabled). Search did not exist: new `SearchText` box (44 px high) over the tiles, matching name or code, case-insensitive, across all categories; while it has text the category selection is ignored.
- Web/Android: new `FavouritesStore` (Client.Shared, scoped, `IBrowserStorage` key `hotelpos.favourites`) with the same rules and caps; `OrderBuilderPage` has the "★ Favourites" chip (`#chip-favourites`), a search box (`#menu-search`, `oninput`) and a star button beside each item card (a sibling of the card, because a button cannot nest in a button). The cart is counted after a confirmed send.
- No API, permission or matrix change. Favourites are per terminal / browser profile, so a different device or browser starts empty.
- Tests: `Desktop.Tests/Orders/OrderBuilderViewModelTests` (+5 methods: pin/unpin persisted and chip placement, favourites ordering, counting and cap on success, no count on rejection, search theory of 4), `FakeUserPreferences` extended. `Client.Tests/FavouritesTests` (7: pin/unpin, pinned-then-most-ordered order, search theory of 4, store cap). The page tests wait 500 ms after the first render; without it the first event is lost while the page is still starting up (realtime connect attempt).
