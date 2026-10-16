# Phase 18 — Web Clients for Waiter and Kitchen (PWA)

Status: In progress (code complete 2026-10-15; demo on real phones and tablets open) · Depends on: Phases 9 (permissions, devices), 12 (CQRS), 16 (hosting), 17 (design tokens) ·
References: `docs/08-multi-device-readiness-audit.md` (source-grounded audit), `docs/07-menus-screens-and-fields.md` (functional
reference), owner's master prompt of 2026-10-15

## 1. Goal

Give waiters a phone/tablet client and kitchen staff a tablet client **without a second backend**: a responsive Progressive Web
App served by the existing API under `/app`, using the existing REST endpoints, permission policies, JWT, device registry and
`RestaurantHub` events unchanged. Cashiers, managers and admins keep the WPF application (printing, cash drawer, reports). One
shared business backend, role-specific interfaces, device-aware presentation, server-enforced security.

## 2. Prerequisites

- Audit findings in `docs/08` § 2–6: the backend is reusable as-is; the gaps are a client, same-origin hosting, HTTPS, a browser
  token policy and browser device identity.
- Owner decisions in § 13 confirmed (technology, browser authentication, HTTPS certificate approach).
- A server with HTTPS: PWA features (install, service worker, offline cache) require HTTPS except on `localhost`.

## 3. Scope

**In**
- New project `src/HotelPOS.Web` — Blazor WebAssembly PWA (decision § 13-A), referencing `HotelPOS.Contracts`.
- Hosting from the API under `/app` (same origin, no CORS), with SPA fallback limited to `/app/**`.
- Browser authentication: access token in memory; refresh token in an `HttpOnly; Secure; SameSite=Strict` cookie issued by
  **new, additive** endpoints `POST /api/auth/web/login`, `POST /api/auth/web/refresh`, `POST /api/auth/web/logout` that call
  the existing `Login`, `RefreshToken` and `Logout` commands (decision § 13-B). Existing endpoints and DTOs unchanged.
- Browser device identity: a stable per-browser device name `WEB-<ROLE>-<6 hex>` generated once and kept in `localStorage`
  (not a secret), sent on login exactly as WPF sends `DeviceName`/`DeviceType`; the device appears in Administration › Devices
  and can be renamed or deactivated there.
- Waiter screens: Sign in, Tables (map + section filter), Table details, Order Builder (categories, item cards with pictures,
  options pop-up, notes, quantity, guests), My Orders, kitchen progress and Request bill. Local drafts per table.
- Kitchen screens: Sign in, Station selection (joins the hub `station:{id}` group), Kitchen Display (New / Preparing / Ready),
  Ticket details, Accept / Start / Ready / Done / Recall where the user's permissions allow, Completed list.
- SignalR from the browser using the existing hub, token via `AccessTokenProvider` (→ `access_token` query), automatic
  reconnect, resync on reconnect, explicit connection status.
- Idempotency: one GUID per logical write, reused on every retry (mirrors `OrderSubmitter`).
- Shared web design system: the Phase 17 tokens (`docs/phases/phase-17 § 8.1`) as CSS custom properties; mobile-first waiter
  layout; tablet-landscape kitchen layout.
- Tests: bUnit component tests, API tests for the web auth endpoints and SPA fallback, authorization-matrix rows, the existing
  suites unchanged and green.
- Docs: `docs/01` (architecture), `03` (API reference § web auth), `05` (clients), `ops/01`, `02` (HTTPS mandatory for PWA),
  `07` (go-live rows for phones/tablets), `08` (kept as the audit record).

**Out**
- Cashier, manager or admin web screens; any printing from a browser; payments, discounts, refunds, shifts, day end, GST,
  reports, settings in the web client.
- Schema changes, new business rules, new hub events (none proved necessary by the audit), changes to existing DTOs or
  routes, CORS (not needed with same-origin hosting), native Android/iOS apps.

## 4. Deliverables by project

| Project | Deliverable |
|---|---|
| `src/HotelPOS.Web` (new) | Blazor WASM PWA: `Program.cs`, `App.razor`, `Routes` with role-aware redirect, `Layout/` (waiter shell, kitchen shell), `Services/` (`WebApiClient`, `WebAuthState`, `WebRealtimeClient`, `DeviceIdentity`, `DraftStore`, `IdempotentSender`, `ConnectionStatus`), `Pages/Waiter/*`, `Pages/Kitchen/*`, `Components/` (ItemCard, StatusChip, TicketCard, NumberStepper, OptionsSheet, Toast), `wwwroot/` (`index.html` with `<base href="/app/">`, `manifest.webmanifest`, `service-worker.js`, `css/tokens.css`, `css/app.css`, `js/storage.js`) |
| `src/HotelPOS.Api` | `Controllers/WebAuthController.cs` (3 endpoints, cookie handling); `Program.cs`: `UseBlazorFrameworkFiles("/app")`, default `UseStaticFiles()`, `MapFallbackToFile("/app/{*path:nonfile}", "app/index.html")` placed **after** controllers and hub; `HotelPOS.Api.csproj`: reference to Web + `Microsoft.AspNetCore.Components.WebAssembly.Server`; `appsettings.Production.template.json`: HTTPS URL |
| `src/HotelPOS.Application` | No handler changes. (If the refresh command needs the device claim from the cookie flow, it is passed through the existing request DTO.) |
| `src/HotelPOS.Contracts` | No changes (DTOs, `HubEvents`, `Permissions`, `PosHeaders`, `PosJson` reused). |
| `tests/HotelPOS.Web.Tests` (new) | bUnit: role routing, order builder rules (quantities, option group rules), idempotent sender reuse of keys, draft store round-trip, reconnection status |
| `tests/HotelPOS.Api.Tests` | Web auth endpoints (cookie set/rotated/cleared, rejected without cookie, device claim present), `/app` fallback does not shadow `/api/**` or `/hub/**`, authorization matrix rows |
| `tools` / `publish.ps1` | Web app published into the server package (`wwwroot/app`), version stamped |
| `HotelPOS.sln` | Two new projects |

## 5. Data model

None. Device rows are created through the existing `DeviceRegistry.UpsertAsync` (by name).

## 6. API endpoints (additive only)

| Route | Roles / policy | Behaviour |
|---|---|---|
| `POST /api/auth/web/login` | anonymous (rate-limited like `/api/auth/login`) | Runs the existing `LoginCommand`; returns `LoginResponse` **without** the refresh token in the body; sets cookie `hp_rt` (`HttpOnly; Secure; SameSite=Strict; Path=/api/auth/web`) with the refresh token |
| `POST /api/auth/web/refresh` | anonymous + cookie + header `X-Requested-With: HotelPOS.Web` | Reads `hp_rt`, runs the existing `RefreshTokenCommand` (rotation and revocation unchanged), rewrites the cookie, returns the new access token |
| `POST /api/auth/web/logout` | authenticated | Runs `LogoutCommand`, clears the cookie |

Everything else the web client calls already exists: `api/tables`, `api/tables/{id}/occupy|release`, `api/orders` (+ items,
submit, serve, cancel, request-bill), `api/kitchen/orders` (+ accept, start, ready, complete, recall), `api/menu` (compact menu
and images), `api/settings`, `api/system/info`, `api/auth/me`.

## 7. Real-time events

No new events. Subscriptions: waiter — `TableStatusChanged`, `OrderCreated/Updated/Cancelled`, `OrderAccepted/Preparing/Ready/
Served`, `BillRequested`, `BillUpdated`, `PaymentCompleted`, `MenuChanged`, `ServerNotice`; kitchen — `KitchenTicketCreated/
Updated`, `MenuChanged`, `ServerNotice`, plus hub methods `JoinStation`/`LeaveStation`.

## 8. Business rules

None change. Presentation/security rules this phase adds:

- The client reads roles and permissions from `/api/auth/me` (server truth) to build navigation and enable buttons; every
  action is still authorized on the server. A device type never grants a role.
- Access token lives only in memory; on page reload the client calls `/api/auth/web/refresh` (cookie) before rendering.
- Refresh failure (revoked, expired, device deactivated → 403 `DEVICE_DISABLED`) returns the user to Sign in with the reason.
- Role/permission changes take effect on the next sign-in, as in WPF ("Force re-login" revokes sessions → refresh fails).
- Writes carry `Idempotency-Key`; the same key is reused for retries of the same logical operation; a replayed response is
  treated as success. Concurrency conflicts (409) reload the entity and show the server message.
- Drafts: per table in `localStorage` (items, options, notes, guests); restored on reopening the table; cleared on successful
  submit. The waiter is told when a draft exists.
- Connection status is always visible (Connected / Reconnecting / Offline). On reconnect: refresh the current screen's data
  from the API and re-join the kitchen station group.

## 9. Desktop screens (web screens, build order)

| Step | Screens | Notes |
|---|---|---|
| 18.1 Review & hosting | `/app` hosting, HTTPS notes, SPA fallback test | API changes only |
| 18.2 Foundation | Web project, tokens, shells, auth (cookie flow), `/me` routing, API client, realtime client, device identity, idempotent sender, draft store | shared by both roles |
| 18.3 Waiter | Sign in, Tables, Table details, Order Builder + options, My Orders, Request bill | mobile-first, ≥ 44 px targets |
| 18.4 Kitchen | Station selection, Kitchen Display, Ticket details, Completed | tablet landscape first |
| 18.5 Integration | Web waiter → WPF kitchen printer → WPF cashier end-to-end; concurrency and retry drills | |
| 18.6 Production | publish.ps1, HTTPS, docs, go-live rows, device tests | |

## 10. Tests

- Existing suites unchanged and green (Domain 365, Application 246, Api 180 + new, Desktop 248 + 1).
- Api: web auth cookie flow (set, rotate, clear, reject without cookie/header, device claim), `/app` fallback vs `/api` and
  `/hub` precedence, authorization-matrix rows for the three routes.
- Web (bUnit): role routing from `/me`; order builder option-group rules identical to WPF (`docs/07 § 3.2.2`); idempotent
  sender reuses the key across retries and treats `Idempotent-Replayed` as success; draft store round-trip; status banner.
- Manual (§ 11) on real phones and tablets — the only way to verify touch, PWA install and HTTPS trust.

## 11. Manual demo script

1. Server on HTTPS; phone trusts the certificate; open `https://<server>:5001/app`, "Add to Home Screen".
2. Sign in as waiter → device `WEB-WAITER-xxxxxx` appears Online in Administration › Devices.
3. Occupy T05, build an order with options and a note, airplane mode, reconnect, send → one ticket on the kitchen tablet and
   one KOT on the kitchen Windows PC.
4. Kitchen tablet: pick station, Accept → Start → Ready; waiter's phone shows Ready; Done from the tablet.
5. Request bill from the phone → WPF cashier queue; claim, split payment, finalize, print; table frees on all devices.
6. Deactivate the phone's device → immediate disconnect, `DEVICE_DISABLED` on next call.
7. Change a waiter permission with "Force re-login" → phone returns to Sign in.
8. Unplug the server network for 60 s → all clients reconnect, no duplicates.
9. Portrait and landscape on the kitchen tablet; one-handed use on the phone.

## 12. Acceptance criteria

- [ ] Waiter signs in on a mobile browser and submits an order persisted by the existing backend.
- [ ] Authorized kitchen tablet receives the ticket via SignalR; status changes reach the waiter's phone.
- [ ] WPF cashier receives the bill request and claims, finalizes, pays and prints unchanged.
- [ ] Table and order state consistent across web, WPF and the database.
- [x] Unauthorized roles cannot reach protected routes or screens (server-enforced; `WebFlowTests` proves kitchen ↔ waiter 403s); deactivated devices lose access (existing middleware + hub abort); refresh fails after
      "Force re-login".
- [x] Retries produce no duplicate orders (`WebFlowTests` replays the same key and gets the stored answer; `IdempotentSenderTests`); reconnect drills remain in the device demo.
- [ ] Works on the restaurant LAN via the configured server address over HTTPS.
- [x] No schema, business-rule, DTO or existing-route change; existing suites green; new tests green (Api +14, Web 23).
- [ ] Definition of Done satisfied; manual demo run.

## 13. Risks, notes and owner decisions (►)

- ► **A. Technology — Blazor WebAssembly (recommended) vs React/TypeScript.** Blazor shares `HotelPOS.Contracts`
  (DTOs, `HubEvents`, `Permissions`, `PosJson`) and the same SignalR client package with WPF, stays in one language and one
  test stack, and needs no Node toolchain in `publish.ps1`. Cost: first load ~2–3 MB (cached by the PWA; LAN only) and ~1–2 s
  start on low-end phones. React gives a lighter first load and a larger mobile UI ecosystem but duplicates DTO types and adds
  a second build chain.
- ► **B. Browser authentication — HttpOnly cookie refresh via three additive endpoints (recommended).** Avoids refresh
  tokens in `localStorage`; reuses the existing refresh rotation/revocation; CSRF limited by `SameSite=Strict`, a custom
  header and a narrow cookie path. Alternative: memory-only tokens (no backend change) — the waiter must sign in again after
  every page reload or phone sleep that evicts the tab; rejected for usability.
- ► **C. HTTPS on the LAN.** Options: (1) internal CA — one root certificate installed on each phone/tablet (documented
  step, no internet needed); (2) a public hostname with a real certificate (needs DNS and renewal). Recommend (1), with the
  CA creation added to `configure-server.ps1`/docs. Without HTTPS the client still works as a plain page (no install, no
  service worker).
- **Device names.** Generated per browser profile; clearing site data creates a new device row (old one stays, inactive after
  the admin deactivates it). Acceptable; same as reinstalling WPF with a new name.
- **Printing.** None from the browser. KOTs print from the kitchen Windows PC running WPF (auto-print on
  `KitchenTicketCreated`); invoices/receipts from the WPF cashier. Documented, not changed.
- **Role groups fixed at connect time.** Same as WPF; the web client reconnects after re-login.
- **Fallback precedence.** `MapFallbackToFile` is scoped to `/app/{*path:nonfile}` and registered last, so `/api/**`, `/hub/**`,
  `/health`, `/media/**`, `/updates/**` are unaffected (verified by a new Api test).
- **Token budget.** Built step by step on one model, like Phase 17; bUnit and Api tests as the safety net; device screenshots
  only from the manual demo.

## 14. Changes during implementation

_(filled per step)_

### 18.1 Browser sign-in endpoints (2026-10-15)

- Reordered against § 9: the `/app` hosting lines need the web project to exist, so they move to 18.2 together with the
  project and the fallback-precedence test. 18.1 delivers the browser sign-in.
- `src/HotelPOS.Api/Controllers/WebAuthController.cs` — `POST api/auth/web/login|refresh|logout`. Same `LoginCommand`,
  `RefreshTokenCommand`, `LogoutCommand` as the desktop endpoints; the refresh token is removed from the response body and
  carried in cookie `hp_rt` (`HttpOnly; SameSite=Strict; Path=/api/auth/web; Secure` when the request is HTTPS). Refresh
  additionally requires header `X-Requested-With: HotelPOS.Web`; a failed refresh clears the cookie. No Contracts, handler,
  DTO or schema change. Rotation and reuse detection are the existing handler's (`RefreshToken.cs`).
- `LoginRateLimitingMiddleware`: `/api/auth/web/login` added to the guarded paths (same limiter as `/api/auth/login`).
- Authorization matrix unchanged: all three routes are anonymous by design, like `/api/auth/login|refresh|logout`.
- Tests: `tests/HotelPOS.Api.Tests/WebAuthTests.cs` (6): cookie attributes and no token in the body; rotation + replay
  refused; header required; no cookie → 401; logout clears and revokes; wrong password → 401 without cookie. The test client
  runs with `HandleCookies = false` so the rotated cookie cannot leak in from the client's cookie jar.
- Build note: pass an **absolute** `-p:OutDir` inside the repo; a relative one is applied per project and creates
  `src/*/tests/...` output folders that break the Api build (removed).

### 18.2 Foundation: project, hosting, sign-in, services (2026-10-15)

- New `src/HotelPOS.Web` (Blazor WebAssembly PWA, `StaticWebAssetBasePath=app`, `<base href="/app/">`), referencing only
  `HotelPOS.Contracts`; packages pinned centrally at 8.0.32 (the template's 8.0.25 `WebAssembly.Server` carries NU1903).
- API hosting: `HotelPOS.Api.csproj` references the web project and `Microsoft.AspNetCore.Components.WebAssembly.Server`;
  `Program.cs` adds `UseStaticWebAssets()` (no-op after publish), `UseBlazorFrameworkFiles("/app")`, `UseStaticFiles()`
  and, last, `MapFallbackToFile("/app/{*path:nonfile}", "app/index.html").AllowAnonymous()` — the explicit
  `AllowAnonymous` is required because the API's authorization fallback policy demands an authenticated user.
- Services: `WebAuthState` (AuthenticationStateProvider; access token in memory, cookie refresh with single-flight,
  `/me` roles and permissions → claims), `WebApiClient` (envelope, bearer, `Idempotency-Key`, one refresh-and-retry on
  401), `IdempotentSender` (same key across retries of unknown outcomes), `DraftStore` (per-table drafts in localStorage,
  pending key kept), `DeviceIdentity` (`WEB-WAITER-xxxxxx` / `WEB-KITCHEN-xxxxxx`, generated once per browser profile),
  `WebRealtimeClient` (existing hub, token via `AccessTokenProvider`, forever retry, station group re-joined on
  reconnect), `ShellState`, `ToastService`, `IBrowserStorage` (JS interop over localStorage).
- Shell: `MainLayout` (top bar with title, connection pill, user/sign-out; bottom nav per role), `SignIn` (device type
  toggle Waiter/Kitchen, server status, reasons for a session that ended), `Home` routes by server-reported permissions
  (tables for waiters, kitchen for kitchen, otherwise "use the desktop application"). Design tokens mirror Phase 17.
- Tests: `tests/HotelPOS.Web.Tests` (7: idempotent sender key reuse / final answers / give-up; draft round-trip, corrupt
  draft dropped, device names, preferred type) and `WebHostingTests` (7: `/app`, `/app/`, deep links serve the shell;
  `/api/**`, `/hubs/**`, other paths are not caught). The test host uses a stand-in web root (`TestWebRoot`) because a
  redirected OutDir has no static-web-assets manifest; framework-file serving is covered by the manual demo and publish.
- Open for 18.3/18.4: real pages behind `/tables`, `/my-orders`, `/kitchen`, `/kitchen/completed`, `/kitchen/station`.

### 18.3 Waiter screens (2026-10-15)

- `Services/WaiterApi.cs`: the existing routes only — `api/tables` (map), `api/tables/{id}` (+ `occupy`, `release`),
  `api/menu` (cached until `MenuChanged`), `api/orders` (create with `Submit = true`, `{id}/items` append, `serve`,
  `cancel`, `request-bill`), `api/orders/active`, `api/orders/{id}`. Writes that the desktop sends with an
  Idempotency-Key send one here too.
- `Services/OrderComposer.cs`: the cart and its rules as pure code (line merge by item + options + note, quantity and
  guests 1–99, notes ≤ 500, option-group Min/Max wording and checks, draft round-trip, `OrderItemInput` output).
- Pages: `Tables` (section chips, coloured tiles with the desktop legend's meanings, elapsed time from the server clock,
  "Draft on this phone" badge; refreshes on the same events as the desktop map, debounced), `Table details` (occupy
  with a guests stepper, order lines with ticket chips, and the desktop's action rules: New/Resume order, Add items,
  Served, Request bill, Release, Cancel with reason), `Order builder` (category chips, food-image cards with SOLD OUT,
  options bottom sheet, quick notes, cart with steppers, guests, Save draft, Send; the pending key is stored in the
  draft so a retry after a reload reuses it; definite refusals spend the key, unknown outcomes keep it), `My orders`
  (own active orders, Ready first). All pages are permission-gated from the server's `/me`.
- Styles appended to `app.css` (tiles, item cards, cart, sheets, steppers); 4:3 picture cards match Phase 17.
- Tests: `OrderComposerTests` (9 cases) — total web tests 16. Razor lessons: no `section` loop variable, no `>` or
  nested quotes inside inline attribute expressions, lambda parameters must not be `_` when the body discards.

### 18.4 Kitchen screens (2026-10-15)

- `Services/KitchenApi.cs`: `api/kitchen/orders[?stationId]`, `…/completed`, `…/{id}/accept|start|ready|complete|recall`,
  `api/stations`, `api/settings` (warn/late minutes); the chosen station is kept on the tablet (`hotelpos.kitchen.station`).
- `Services/KitchenRules.cs`: timing (amber after warn, red after late, late never below warn — as the desktop),
  elapsed text, column per status (NEW = New + Accepted), buttons per status (Accept/Start, Start, Ready, Done/Recall),
  "+ADD" for batch > 1.
- Pages: `Kitchen Display` (station chips, three columns, large tickets with notes and struck cancelled lines, 52 px
  buttons, 30 s re-colour tick, hub station group joined/left with the choice), `Completed` (today's tickets with wait,
  prep and total minutes), `Station` (first-run choice). Landscape: three columns from 720 px; portrait: stacked.
- Tests: `KitchenRulesTests` (7 cases) — web tests total 23.

### 18.5 Integration (2026-10-15)

- `tests/HotelPOS.Api.Tests/WebFlowTests.cs` drives the web pages' exact calls end to end against the existing API:
  browser sign-in (cookie) for `waiter1` and `kitchen1` → table map and menu → occupy → create + submit with an
  Idempotency-Key (201) → the same request replayed returns the stored answer (`Idempotent-Replayed`, same order) →
  the kitchen lists the ticket for its station and accepts it → the waiter's reload shows the ticket Accepted →
  a kitchen user is refused `POST api/orders` and a waiter is refused `…/start` (403: the server decides, not the
  device type). No new endpoint or rule was needed; the same database rows serve WPF and web.
- Printing path unchanged: the kitchen Windows PC auto-prints on `KitchenTicketCreated`, the billing PC on finalize.
  The simultaneous-ordering and reconnection drills are in the device demo (§ 11).

### 18.6 Production readiness (2026-10-15)

- `dotnet publish src/HotelPOS.Api -c Release` now places the client under `wwwroot/app` (index.html with
  `<base href="/app/">`, manifest, service worker, 197 framework files, 16.5 MB on disk including the pre-compressed
  `.br`/`.gz` copies). The `win-x64 --self-contained` shape used by `build/publish.ps1` is checked the same way, so
  the script needs no change: the server package already carries the web client.
- `scripts/new-lan-certificate.ps1` (new): internal root CA "HotelPOS Root CA" (10 y) + server certificate (2 y),
  exports `hotelpos.pfx` for Kestrel and `hotelpos-root-ca.cer` for phones, tablets and terminals.
- Docs: `ops/02 § 5` (HTTPS required for the browser client; CA script; per-device install steps for Android,
  iPhone/iPad and Windows), `ops/01 § 5a` (phones and tablets), `ops/07` (three go-live rows), `01` § 1 and `05` § 11
  (browser client), `03` § 3.1 (web auth routes). Status tables: In progress (code complete; device demo open).
- Not done here (needs real devices): § 11 manual demo, PWA install and certificate trust on Android/iOS, 125 %
  Windows tablets, airplane-mode and server-outage drills.

## Web accessibility rules (Phase 20.5)

- Every interactive control shows the `:focus-visible` ring (`--focus`); never set `outline:none`.
- Loading uses `<Skeleton>`, failures `<ErrorState>` (with Retry), empty lists `<EmptyState>`; do not hand-write "Loading…" divs.
- Icon-only buttons need `aria-label`; decorative glyphs get `aria-hidden="true"`; each `nav` has an `aria-label`; the active link carries `aria-current="page"`.
- Toasts live in an `aria-live="polite"` region. Theme (system/light/dark) is chosen with the top-bar toggle and stored as `hotelpos.theme`.
