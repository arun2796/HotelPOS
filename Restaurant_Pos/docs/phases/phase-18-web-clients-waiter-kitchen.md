# Phase 18 — Web Clients for Waiter and Kitchen (PWA)

Status: Not started (specified 2026-10-15) · Depends on: Phases 9 (permissions, devices), 12 (CQRS), 16 (hosting), 17 (design tokens) ·
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
- [ ] Unauthorized roles cannot reach protected routes or screens; deactivated devices lose access; refresh fails after
      "Force re-login".
- [ ] Retries and reconnects produce no duplicate orders or payments (idempotency verified by test and demo).
- [ ] Works on the restaurant LAN via the configured server address over HTTPS.
- [ ] No schema, business-rule, DTO or existing-route change; existing suites green; new tests green.
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
