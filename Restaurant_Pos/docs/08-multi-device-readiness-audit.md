# HotelPOS — Multi-device Readiness Audit

Audit date: 2026-10-15 · Branch `main` at `cab212c` (Phases 1–17 merged) · Read-only: no project file was changed by the audit.

Purpose: determine whether the current code can support (1) waiters on Android/iPhone, (2) kitchen staff on Android/iPad/Windows
tablets, (3) cashiers on Windows laptops/desktops or tablets, (4) all devices on one restaurant Wi-Fi/LAN, (5) real-time order,
kitchen, table and billing updates — and, if not, the smallest safe path to get there while reusing the existing application.

Functional reference: `docs/07-menus-screens-and-fields.md`. Every implementation claim below was verified in source; paths are
relative to `Restaurant_Pos/`. Anything not confirmed from code is marked **UNVERIFIED**.

---

## 1. Summary

| Question | Answer |
|---|---|
| Can waiters use Android/iPhone today? | **No.** The only client is a WPF Windows application. The backend is ready for a new client. |
| Can kitchen staff use Android/iPad tablets today? | **No** (same reason). Windows tablets: **yes**. |
| Can cashiers use Windows laptop/desktop/tablet today? | **Yes.** Android/iPad for cashiers: no, and printing would need a new design. |
| Do all devices work together over one LAN? | **Yes** for Windows terminals: one API on port 5000, one PostgreSQL, SignalR groups per role, user, device and station. |
| Real-time updates? | **Yes.** 24 hub events, published after the database transaction commits, delivered to role/user/station groups. |
| Smallest path to phones/tablets | A responsive web/PWA client hosted by the existing API (same origin → no CORS), using the existing REST + JWT + SignalR contract unchanged; keep WPF on the Windows PCs that print. |

---

## 2. What the code is (verified inventory)

### 2.1 Projects (`HotelPOS.sln`)

| Project | Role | Notes |
|---|---|---|
| `src/HotelPOS.Contracts` | DTOs, enums, event names, permissions | `net8.0`, **no package references** — reusable by any .NET client |
| `src/HotelPOS.Domain` | Entities and rules | |
| `src/HotelPOS.Application` | MediatR 12 commands/queries, pipeline behaviours, event publishing | |
| `src/HotelPOS.Infrastructure` | EF Core + Npgsql, migrations, seed, documents (PDF/Excel) | |
| `src/HotelPOS.Api` | ASP.NET Core 8 Web API + SignalR hub | Windows Service host |
| `src/HotelPOS.Desktop` | **WPF** client, `net8.0-windows` | the only client |
| `tests/*` (4) | Domain 365, Application 246, Api 180, Desktop 248 + 1 skipped (all green at audit time) | |
| `tools/HotelPOS.LoadTest`, `tools/HotelPOS.LicenseGenerator` | Ops tools | |

### 2.2 Backend

| Area | Finding | Evidence |
|---|---|---|
| Host | Kestrel, `"Urls": "http://0.0.0.0:5000"`; `UseWindowsService("HotelPOS API")` | `src/HotelPOS.Api/appsettings.json:2`; `Program.cs:25` |
| Middleware order | Swagger (Development only) → static files (menu images `/media…`, installers `/updates`) → `LoginRateLimitingMiddleware` → Authentication → `RequestContextLoggingMiddleware` → Authorization → `AccessEnforcementMiddleware` → `LicenseEnforcementMiddleware` → `IdempotencyMiddleware` → controllers, hub, `/health` | `Program.cs:88–125` |
| Database | PostgreSQL via `UseNpgsql`; `Database:AutoMigrate = true`; connection string from configuration/user-secrets | `src/HotelPOS.Infrastructure/DependencyInjection.cs:42`; `appsettings.json` |
| CQRS pipeline | `LoggingBehavior → ValidationBehavior → AuthorizationBehavior → BusinessDayLockBehavior → TransactionBehavior (+ AfterCommitQueue) → AuditBehavior (SaveChangesAsync)` | `src/HotelPOS.Application/Common/Behaviors/*.cs`; `Common/Cqrs/IAfterCommit.cs` |
| Authentication | JWT bearer; access token 60 min, refresh token 12 h; hub accepts the token from the `access_token` query string | `src/HotelPOS.Api/Common/ApiServiceRegistration.cs:123–155`; `appsettings.json:9–15` |
| Authorization | Claims `sub, name, display_name, role, perm, device_id, device_name` (`PosClaimTypes`); one authorization policy per permission string; controllers `[Authorize(Policy = Permissions.X)]` or `[Authorize(Roles = …)]`; `AuthorizationBehavior` enforces `IRequirePermission` on commands | `ApiServiceRegistration.cs:170–177`; `Common/Behaviors/AuthorizationBehavior.cs` |
| Access enforcement | 403 `DEVICE_DISABLED` for a deactivated device, 403 `ACCOUNT_DISABLED` for a disabled user, checked per request with caches | `src/HotelPOS.Api/Common/AccessEnforcementMiddleware.cs:21–52` |
| Idempotency | `Idempotency-Key` (GUID) header required on marked routes; stored response replayed with header `Idempotent-Replayed: true` | `src/HotelPOS.Api/Common/IdempotencyMiddleware.cs:15–52`; `IIdempotencyStore`; `Domain/Administration/IdempotencyRecord.cs` |
| Concurrency | `RowVersion` on Bill, Table, KitchenOrder, MenuItem, User, Shift and cash entities | `src/HotelPOS.Infrastructure/Persistence/Configurations/*Configurations.cs` |
| Bill claims | `Bill.ClaimedByUserId / ClaimedByDeviceId / ClaimExpiresAt`, `IsHeldByOther`; `ReleaseExpiredBillClaims`; manager take-over | `src/HotelPOS.Domain/Billing/Bill.cs:71–103`; `Application/Features/Billing/Commands/ClaimBill.cs`, `ReleaseExpiredBillClaims.cs` |
| Device registry | Login carries `DeviceName`/`DeviceType`; `DeviceRegistry.UpsertAsync` finds-or-creates **by name** and the JWT receives `device_id`; `POST /api/devices/register`; admin `GET/PUT /api/admin/devices`, `…/deactivate`, `…/activate` | `Application/Features/Auth/Commands/Login.cs:30–59`; `Features/Devices/DeviceRegistry.cs:25–40`; `Api/Controllers/SystemControllers.cs:48–55`; `AdminDevicesController.cs` |
| HTTPS | Optional; plain HTTP documented as acceptable on an isolated staff network | `docs/ops/02-lan-and-firewall.md:36–60` |
| CORS | **Not configured anywhere** in `src/HotelPOS.Api` | search result |

### 2.3 SignalR

| Area | Finding | Evidence |
|---|---|---|
| Hub | `RestaurantHub` at `HubRoutes.Restaurant`, `[Authorize]`; keep-alive 15 s, client timeout 30 s, `PosJson` protocol | `src/HotelPOS.Api/Hubs/RestaurantHub.cs:15`; `ApiServiceRegistration.cs:56–63` |
| Connect | Adds connection to `role:{role}` for each role, `user:{id}`, `device:{guid}`; tracks presence (`ConnectionTracker`); **aborts** a deactivated device; publishes `DeviceStatusChanged` | `RestaurantHub.cs:38–93` |
| Station groups | Client methods `JoinStation(int)` / `LeaveStation(int)` → `station:{id}` | `RestaurantHub.cs:119–130`; `Hubs/HubGroups.cs` |
| Publisher | `IRealtimeNotifier.PublishAsync(eventName, payload, RealtimeAudience)`; audience = Everyone / Roles / UserIds / StationIds; `SignalRRealtimeNotifier` maps to groups | `src/HotelPOS.Application/Common/Interfaces/IRealtimeNotifier.cs`; `src/HotelPOS.Api/Hubs/SignalRRealtimeNotifier.cs:17–36` |
| After commit | Handlers enqueue publishes in `AfterCommitQueue`; `TransactionBehavior` flushes after the commit, so clients never see uncommitted state | `Common/Cqrs/IAfterCommit.cs`; `Common/Behaviors/TransactionBehavior.cs` |
| Event names (24) | `TableStatusChanged, MenuChanged, OrderCreated, OrderUpdated, OrderCancelled, KitchenTicketCreated, KitchenTicketUpdated, OrderAccepted, OrderPreparing, OrderReady, OrderServed, BillRequested, BillUpdated, PaymentCompleted, DeviceStatusChanged, ServerNotice, TemplateActivated, DocumentPrinted, ShiftChanged, BusinessDayClosed, BusinessDayReopened, PeriodStatusChanged, Ping` (+ hub methods `JoinStation`, `LeaveStation`) | `src/HotelPOS.Contracts/Realtime/HubEvents.cs` |
| Publishers | `Features/Floor/TableEvents.cs` (table status, Everyone); `Features/Orders/OrderEvents.cs` (orders → Kitchen, Cashier, Manager, Admin); `Features/Kitchen/KitchenSync.cs` (tickets → `station` + `kitchen` role; order progress → Waiter, Cashier, Manager roles **and** the order's waiter `user:{id}`); `Features/Billing/BillEvents.cs` (bills → Cashier, Manager, Admin; payment → Cashier, Waiter, Manager, Admin) | lines cited in § 4 |

### 2.4 Desktop client (WPF)

| Area | Finding | Evidence |
|---|---|---|
| Technology | WPF, `net8.0-windows`, CommunityToolkit.Mvvm, MaterialDesign, SignalR client, Serilog; **no database driver** | `src/HotelPOS.Desktop/HotelPOS.Desktop.csproj:5–25` |
| Terminal configuration | `ClientSettings { ApiBaseUrl, DeviceName, DeviceType (Waiter/Kitchen/Billing/Admin), Density }` saved on the PC; `IsConfigured` requires URL + device name; first-run screen validates with `ApiUrl.TryNormalize` | `Services/Configuration/ClientSettings.cs:9–35`; `ClientSettingsService.cs:57`; `Modules/Config/ConfigurationViewModel.cs:104,156` |
| Base URL | All calls use `settings.Current.ApiBaseUrl`; no `localhost` in `Services/` | `Services/Api/ApiClient.cs:107,138`; `Services/Realtime/RealtimeClient.cs:177`; `Services/Menu/MenuCache.cs:135`; `Services/Updates/UpdateService.cs:72` |
| Auth | `AuthDelegatingHandler` adds `Bearer`, refreshes and retries once on 401 | `Services/Api/AuthDelegatingHandler.cs:39,57` |
| Real-time | `HubConnectionBuilder` with `AccessTokenProvider`, `WithAutomaticReconnect(ForeverRetryPolicy)`; `Reconnecting/Reconnected/Closed` surfaced as status; `Subscribe<T>(eventName, handler)`; joins/leaves station groups | `Services/Realtime/RealtimeClient.cs:125–314` |
| Resync | On `Reconnected`: `ShellViewModel.OnReconnected` reloads the restaurant name and calls `RefreshAsync` on the current page when it is `IRefreshable`; `MenuCache` refreshes; `ShiftBarViewModel` refreshes | `Shell/ShellViewModel.cs:410–415`; `Services/Menu/MenuCache.cs:160`; `Modules/Cash/ShiftBarViewModel.cs:130` |
| Offline | Drafts saved per table as files under `<UserData>/drafts` (`LocalDraftStore`); `OrderSubmitter.SendAsync` retries unconfirmed requests with the **same** idempotency key over `DefaultRetryDelays` | `Services/Orders/LocalDraftStore.cs:56–70`; `Services/Orders/OrderSubmitter.cs:25–55` |
| Density / touch | Comfort (≥ 44 px targets) for Waiter and Kitchen terminals, Compact for Billing/Admin, overridable | `Services/Ui/ThemeService.cs:30–49` |
| Printing | **Client-side, Windows-only**: ESC/POS raw via `winspool.drv` P/Invoke, network printers via `TcpClient` (port 9100), Windows printers via FlowDocument; server renders PDFs | `Services/Printing/PrinterChannels.cs:29,54,111–119`; `FlowDocumentRenderer.cs`; `Api/Controllers/PrintController.cs` |
| E2E coverage | `ServerOutage_ClientReportsIt_ReconnectsAutomatically_AndResyncs`; 73-screen rendering test | `tests/HotelPOS.Desktop.Tests/EndToEnd/ConnectionResilienceTests.cs`; `Rendering/ScreenRenderingTests.cs` |

### 2.5 Other clients

**None.** There is no web, Blazor, MAUI/Xamarin or static SPA project. The API serves static files only for menu images and client
installers (`Program.cs:97–110`); Swagger is enabled only in Development (`Program.cs:88–92`).

---

## 3. Architecture diagram (from code)

```
 Waiter PC / Windows tablet      Kitchen PC / Windows tablet      Billing PC (WPF)        Admin PC (WPF)
 (WPF, DeviceType=Waiter)        (WPF, DeviceType=Kitchen)        + winspool / TCP 9100   + backups, licence
 ClientSettings: ApiBaseUrl,     + hub JoinStation(stationId)       printers
 DeviceName, Density
        │                                │                              │                     │
        │   REST  (Authorization: Bearer <JWT>, Idempotency-Key on writes)                     │
        │   WebSocket  /hub/restaurant?access_token=<JWT>                                      │
        └────────────────────────────────┴──────────────┬───────────────┴─────────────────────┘
                                                        ▼   restaurant Wi-Fi / LAN, TCP 5000 (5001 if HTTPS)
                                        HotelPOS API — Kestrel, Windows Service
   Program.cs:  LoginRateLimit → Authentication(JWT) → Authorization(policies per permission)
                → AccessEnforcement(device/account disabled) → LicenseEnforcement → Idempotency
                → Controllers  |  RestaurantHub [Authorize]
   MediatR:     Logging → Validation → Authorization → BusinessDayLock → Transaction(+AfterCommit) → Audit(SaveChanges)
   Events:      IRealtimeNotifier → SignalRRealtimeNotifier → groups  role:* | user:* | device:* | station:*
                                                        │ EF Core (Npgsql)
                                                        ▼
                                           PostgreSQL — reachable from the server only
```

---

## 4. Device compatibility matrix

| Combination | Classification | Evidence and notes |
|---|---|---|
| WPF client on a Windows laptop | **WORKS IN CURRENT CODE** | WPF `net8.0-windows`; Compact density for Billing/Admin; installers from `publish.ps1` (Phase 10) |
| WPF client on a Windows tablet | **WORKS IN CURRENT CODE** (confirm by hand) | Comfort density, touch targets ≥ 44 px (Phases 11, 17). The rendering harness is 1366 px wide: 125 % scaling, on-screen keyboard and portrait use are untested |
| Existing application on Android | **NOT SUPPORTED BY THE CURRENT CLIENT TECHNOLOGY** | WPF runs only on Windows; no other client project exists |
| Existing application on iPhone | **NOT SUPPORTED BY THE CURRENT CLIENT TECHNOLOGY** | Same |
| Android / iPad tablet through a browser | **NOT SUPPORTED BY THE CURRENT CLIENT TECHNOLOGY** | No web UI is served; only menu images and installers are static files |
| Existing backend used by a future responsive web / PWA client | **PARTIALLY IMPLEMENTED** (backend) · **REQUIRES DEVELOPMENT** (client) | Reusable unchanged: REST with permission policies, JWT + refresh, hub token via query string, role/user/station groups, idempotency, device registration, menu images. Missing: CORS (only needed if the SPA is on another origin), SPA hosting, HTTPS by default, a browser token-storage policy, and any printing path |

---

## 5. Order workflow trace (code-verified)

```
Waiter   POST api/tables/{id}/occupy ............ TableStatusChanged → Everyone
Waiter   POST api/orders (+Idempotency-Key) ..... OrderCreated → Kitchen, Cashier, Manager, Admin
Waiter   POST api/orders/{id}/submit ............ KitchenTicketCreated → station:{n} + role:kitchen
Kitchen  POST api/kitchen/orders/{t}/accept|start|ready|complete|recall
                                                   KitchenTicketUpdated → station + kitchen
                                                   OrderAccepted/Preparing/Ready/Served + OrderUpdated
                                                     → Waiter, Cashier, Manager roles + user:{waiterId}
Waiter   POST api/orders/{id}/request-bill ...... BillRequested → Cashier, Manager, Admin
Cashier  POST api/billing/{id}/claim → GET api/billing/{id} → POST …/finalize → POST …/payments (+key)
                                                   BillUpdated(Finalized); PaymentCompleted → Cashier, Waiter, Manager, Admin
Server   AutoCloseOnFullPayment: order.Close, tickets.CloseOnSettle, table.DetachOrder → table free
```

| # | Step | UI command (WPF) | API route and policy | Handler / persistence | Event → receivers |
|---|---|---|---|---|---|
| 1 | Waiter occupies a table | `TableDetailsViewModel.OccupyAsync` (`Modules/Tables/TableDetailsViewModel.cs:227`) | `POST api/tables/{id}/occupy` [`TablesOperate`] (`Controllers/FloorControllers.cs:80`) | `Features/Floor/Commands/OccupyTable.cs`; `Table.RowVersion` (`FloorConfigurations.cs:32`) | `TableEvents.cs:21` → `TableStatusChanged` → `TableMapViewModel:92`, `OrderListViewModels:64` |
| 2 | Waiter creates an order and adds items | `NewOrderAsync` (:283) → Order Builder; draft in `LocalDraftStore` | `POST api/orders`; `PUT api/orders/{id}/items`; `POST api/orders/{id}/items` [`OrdersCreate`]; Idempotency-Key (`Services/Api/OrdersApi.cs:40,55`) | `CreateOrder.cs`, `ReplaceOrderItems.cs`, `AppendOrderItems.cs` | `OrderEvents.cs:27,40` → `OrderCreated` / `OrderUpdated` → Kitchen, Cashier, Manager, Admin |
| 3 | Waiter sends to kitchen | `OrderBuilderViewModel.SendAsync` (:331) → `OrderSubmitter.SendAsync` | `POST api/orders/{id}/submit` [`OrdersCreate`] | `SubmitOrder.cs:73–77`: `order.Submit(now)`, `_db.KitchenOrders.AddRange(tickets)` | `KitchenSync.cs:63–74` → `KitchenTicketCreated` → `station:{id}` + `role:kitchen` |
| 4 | Backend validates and persists | — | `IdempotencyMiddleware` (400 without key; replay on repeat) | Pipeline: Validation → Authorization → BusinessDayLock → Transaction → Audit `SaveChangesAsync` (`AuditBehavior.cs:55`); publishes flushed from `AfterCommitQueue` | — |
| 5 | Kitchen receives the ticket | `KitchenDisplayViewModel` subscribes `KitchenTicketCreated/Updated` (:188–189); station via `RealtimeClient` `JoinStation` (:276–281) ↔ `RestaurantHub.JoinStation` (:119) | `GET api/kitchen/orders` [`KitchenOperate`] on refresh | — | — |
| 6 | New → Preparing → Ready → Done | `AcceptAsync / StartAsync / ReadyAsync / DoneAsync / RecallAsync` (:280–292) | `POST api/kitchen/orders/{t}/accept|start|ready|recall` [`KitchenOperate`]; `…/complete` [Roles Runners] (`KitchenController.cs:36–57`) | `Features/Kitchen/Commands/AcceptKitchenTicket.cs`, `StartKitchenTicket.cs`, `MarkKitchenTicketReady.cs`, `CompleteKitchenTicket.cs`, `RecallKitchenTicket.cs`; `KitchenOrder.RowVersion` | `KitchenSync.cs:79–128` → `KitchenTicketUpdated` (station + kitchen); `OrderAccepted/Preparing/Ready/Served` + `OrderUpdated` → Waiter, Cashier, Manager roles and `user:{order.WaiterId}` |
| 7 | Waiter receives status | `TableMapViewModel:93–99` (`OrderUpdated`, `KitchenTicketUpdated`, progress events); `OrderListViewModels:64–66` refresh | — | — | as step 6 |
| 8 | Waiter requests a bill | `TableDetailsViewModel.RequestBillAsync` (:378) | `POST api/orders/{id}/request-bill` [Roles BillRequesters]; Idempotency-Key (`OrdersApi.cs:67`) | `Features/Billing/Commands/RequestBill.cs` | `BillEvents.PublishRequestedAsync` (:33) → `BillRequested` → Cashier, Manager, Admin → `BillingQueueViewModel:90` |
| 9 | Cashier opens the pending bill | `BillingQueueViewModel.OpenAsync` (:149); `BillDetailViewModel.TakeOverAsync` (:463) | `POST api/billing/{id}/claim`, `GET api/billing/{id}` [`BillingOperate`] (`BillingControllers.cs:35–42`) | `ClaimBill.cs`; `Bill.cs:71–103` claim fields and `IsHeldByOther`; `ReleaseExpiredBillClaims.cs` | `BillUpdated` → queue and bill screens |
| 10 | Cashier collects payment and finalizes | `BillDetailViewModel.FinalizeAsync` (:603), `ConfirmPaymentAsync` (:648) | `POST api/billing/{id}/finalize`; `POST api/billing/{id}/payments` [`BillingOperate`]; Idempotency-Key (`Services/Api/BillingApi.cs:99`) | `FinalizeBill.cs`, `AddPayment.cs` → `BillWorkflow.cs`; `Bill.RowVersion` (`BillingConfigurations.cs:72`) | `BillUpdated(Finalized)` (`BillWorkflow.cs:142`); `PaymentCompleted` → Cashier, Waiter, Manager, Admin (`BillEvents.cs:92–104`) |
| 11 | Table status updates / release | automatic | — | `BillWorkflow.cs:151–164`: when `AutoCloseOnFullPayment` (default on) → `order.Close`, `ticket.CloseOnSettle`, `table.DetachOrder(keepGuestsSeated: false)`. Manual: `POST api/tables/{id}/release` → `ReleaseTable.cs` (only when the table has no order) | `PaymentCompleted` carries the result; whether this path also emits `TableStatusChanged` is **UNVERIFIED** (the table map refreshes on `BillUpdated` as well, `TableMapViewModel:95`) |

---

## 6. Inspection checklist

| Question | Verdict | Evidence |
|---|---|---|
| Clients use the configured server address, not localhost | **Verified** | § 2.4 "Base URL" |
| Multiple terminals can connect to one backend | **Verified** | Kestrel `0.0.0.0:5000`; `ConnectionTracker`; devices keyed by unique name (`DeviceRegistry.UpsertAsync:34`); `tools/HotelPOS.LoadTest` |
| SignalR connections authenticate | **Verified** | `[Authorize]` hub; `OnMessageReceived` reads `access_token` (`ApiServiceRegistration.cs:149–151`); `AccessTokenProvider` (`RealtimeClient.cs:181`); deactivated device → `Context.Abort()` |
| Events reach the correct terminals and roles | **Verified**, one caveat | Audience → groups (`SignalRRealtimeNotifier.cs:27–36`). Group membership comes from the JWT at connect time; after a permission change users must re-login (Roles screen option "Force re-login") |
| Reconnection triggers resynchronisation | **Verified** | `ForeverRetryPolicy`; `ShellViewModel.OnReconnected` → `IRefreshable.RefreshAsync`; `MenuCache` refresh; E2E test `ServerOutage_ClientReportsIt_ReconnectsAutomatically_AndResyncs` |
| Duplicate order submissions are safe | **Verified** | `Idempotency-Key` on create order, items, submit path, request-bill, payments, refunds; middleware replay; `OrderSubmitter` retries with the same key |
| Concurrent edits / bill claims | **Verified** | `RowVersion` across Bill, Table, KitchenOrder, MenuItem, User, Shift, cash entities; time-limited bill claim with manager take-over |
| Disconnection does not silently lose saved orders | **Partially verified** | Drafts persist on disk per table; retries bounded by `DefaultRetryDelays`. Exact UI behaviour once every retry fails is **UNVERIFIED** (the draft file remains and is restored on the next visit per `OrderBuilderViewModel` `_restoring`) |
| API and database run on a local restaurant server | **Verified** | `UseWindowsService`; `docs/ops/01-installation.md`, `02-lan-and-firewall.md`; `AutoMigrate` |
| No client connects directly to PostgreSQL | **Verified** | Desktop references only Contracts and HTTP/SignalR packages |
| Browser clients would need extra configuration | **Yes** | No CORS registration; HTTP by default (HTTPS optional, `docs/ops/02 § 5`); tokens returned in the body (no cookies); `Idempotency-Key` must be sent; no browser printing path |

---

## 7. Verified working components (reusable unchanged)

- REST API with per-permission policies and role checks; `HotelPOS.Contracts` DTOs and `HubEvents`.
- JWT login/refresh/logout, lockout, login rate limiting, manager approval tokens.
- `RestaurantHub` with role, user, device and station groups; presence tracking; device deactivation enforcement.
- After-commit event publishing; the 24-event catalogue with role/user/station audiences.
- Idempotency middleware; optimistic concurrency; bill claims with expiry and take-over.
- Device registry (`/api/devices/register`, admin management).
- Menu image hosting; server-side PDF/Excel documents; client update hosting under `/updates`.
- Windows Service hosting, backups, LAN/firewall scripts and the go-live checklist (`docs/ops/*`).

## 8. Missing components and technical blockers

1. **No non-Windows client.** The only UI is WPF. This alone blocks waiters on phones and kitchen staff on Android/iPad.
2. **Printing is inside the Windows client** (`Services/Printing/PrinterChannels.cs`). A phone or iPad cannot print KOTs, invoices or receipts. Options: keep one Windows PC per kitchen/counter as the printer (it already auto-prints on `KitchenTicketCreated` and on finalize), or add server-side printing later.
3. **No CORS, no SPA hosting, HTTP by default.** A browser client needs the API to host it (same origin) or CORS, and HTTPS once phones join the network.
4. **Device identity is by typed name.** A phone/browser must supply a stable `deviceName` at login (or call `POST /api/devices/register`).
5. **Role groups are fixed at connect time.** Same as WPF today; a web client must reconnect after permission changes.
6. **Refresh tokens in the response body.** Fine for WPF (DPAPI-protected); a browser needs a storage policy (memory + silent refresh, or HttpOnly cookie endpoint — the latter is a backend addition).

## 9. Exact files that would change for mobile and tablet support

Backend — small and additive; no business logic, DTO, schema, billing, GST or permission change:

| File | Change |
|---|---|
| `src/HotelPOS.Api/Common/ApiServiceRegistration.cs` | `AddCors` with the LAN origins **only if** the web client is served from another origin; HTTPS certificate binding for Kestrel |
| `src/HotelPOS.Api/Program.cs` | `UseCors` (same condition); `UseStaticFiles` + `MapFallbackToFile("/app/…")` to host the PWA from the API |
| `src/HotelPOS.Api/appsettings.Production.template.json` | HTTPS URL (5001), allowed origins |
| `docs/ops/02-lan-and-firewall.md`, `07-go-live-checklist.md` | HTTPS required when phones are on the network; QR/URL for staff devices |

New client — no existing file changes:

| Deliverable | Scope |
|---|---|
| Responsive web / PWA project (Blazor WebAssembly recommended: it can reference `HotelPOS.Contracts` directly; React/TypeScript is equally viable) | Waiter: Tables, Table details, Order Builder (rules from `docs/07 § 3.2`), My Orders. Kitchen: Kitchen Display (+ `JoinStation`). Cashier (optional, later): Queue, Bill, Payment |
| Cashier on Windows | Stays on WPF (printing, cash drawer, ESC/POS) |

## 10. Minimum-change implementation plan

1. **Enable HTTPS on the server** (`docs/ops/02 § 5`) and **host the PWA from the API** under `/app` — same origin, so no CORS and the same JWT flow.
2. **Waiter PWA first.** Login via `POST /api/auth/login` with a per-phone `deviceName`; `GET /api/tables`, `POST /api/tables/{id}/occupy`; Order Builder posting `POST /api/orders` + `Idempotency-Key` (new GUID per attempt, reused on retry), `POST …/submit`, `POST …/request-bill`; SignalR with `accessTokenFactory`, subscribing to `TableStatusChanged`, `OrderUpdated`, `OrderReady`, `BillUpdated`. Keep a local draft (IndexedDB) exactly as `LocalDraftStore` does.
3. **Kitchen PWA.** `GET /api/kitchen/orders`, accept/start/ready/complete; call hub `JoinStation(stationId)`; keep one Windows PC per kitchen running WPF as the KOT printer — zero new printing code.
4. **Cashier stays on WPF.** Revisit a cashier PWA only after server-side printing exists.
5. **Hardening.** Browser token policy, device naming per phone, PWA rendering/E2E tests, add the new routes to the authorization-matrix test if any endpoint is added.

## 11. Manual tests required on real devices

1. Phone on the restaurant Wi-Fi opens `https://<server>:5001/app`, signs in as a waiter, and appears Online under Administration › Devices with its name.
2. Occupy T05 on the phone → the table turns Occupied on the billing PC and the kitchen tablet within one second.
3. Build an order with options and a note; switch the phone to airplane mode before sending; reconnect; send → exactly one kitchen ticket (check the `Idempotent-Replayed` header in the server log).
4. Kitchen tablet shows the ticket on the right station only; Accept → Start → Ready → the waiter's phone shows Ready; the KOT prints on the kitchen Windows PC.
5. Request the bill from the phone → it appears in the billing queue; the cashier claims it; a second cashier sees "held by …"; a manager can take over.
6. Split payment, finalize, print the invoice on the Windows PC; the table frees automatically (`AutoCloseOnFullPayment`).
7. Deactivate the phone's device in Administration → the phone is disconnected at once and its next request returns `DEVICE_DISABLED`.
8. Unplug the server's network cable for 60 s → every client shows Reconnecting, then resyncs with no duplicate orders or tickets.
9. Windows tablet at 125 % scaling and 1366×768: Order Builder, Kitchen Display and Bill screens have no clipped controls; the on-screen keyboard does not cover the Send / Confirm buttons.
10. Permission change for the Waiter role with "Force re-login" → the phone is signed out and the new sidebar/menu applies after sign-in.

---

*Prepared from source on 2026-10-15. Functional behaviour reference: `docs/07-menus-screens-and-fields.md`.*
