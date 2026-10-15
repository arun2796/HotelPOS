# 01 — Architecture

Last updated: 2026-10-07

## 1. System architecture

```
                         HOTEL / RESTAURANT Wi-Fi + LAN
                                   |
        +--------------+-----------+-----------+--------------+
        |              |                       |              |
   WAITER-01       KITCHEN-01              BILLING-01     ADMIN PC
   (WPF)           (WPF, KDS)              (WPF)          (WPF)
        |              |                       |              |
        +--------------+-----------+-----------+--------------+
                                   |
                       HTTP (REST, JSON)  +  WebSocket (SignalR)
                                   |
                                   v
                   SERVER PC  (static LAN IP, e.g. 192.168.1.100)
                   +------------------------------------------+
                   |  HotelPOS.Api  (Kestrel, Windows Service) |
                   |    - REST controllers                      |
                   |    - /hubs/restaurant (SignalR)            |
                   |    - JWT auth, validation, audit           |
                   +-------------------+----------------------+
                                       |  EF Core (TCP 1433, localhost only)
                                       v
                   +------------------------------------------+
                   |  PostgreSQL  (hotelpos database)          |
                   +------------------------------------------+
```

Responsibilities:

| Component | Owns |
|---|---|
| WPF client | Presentation, input, local configuration, local draft cache, printing, connection management |
| API | Authentication, authorization, validation, business rules, state machines, calculations, transactions, audit, real-time publishing |
| PostgreSQL | Persistent truth. Only the API process connects to it. |

The desktop app contains **no business rules** beyond UI conveniences (e.g. computing a provisional cart
total for display). The server recalculates everything.

### Browser client (Phase 18)

`src/HotelPOS.Web` is a Blazor WebAssembly progressive web app for waiters (phones) and kitchen staff (tablets). The
API serves it from `/app` (same origin: no CORS), so it calls the same REST routes, the same policies and the same
`RestaurantHub` as the desktop; it shares `HotelPOS.Contracts` for DTOs, event names and permissions. Its only
backend additions are `POST /api/auth/web/login|refresh|logout` (refresh token in an HttpOnly cookie) and the
static hosting lines in `Program.cs`. Billing, management and printing stay on the WPF client. See
`docs/phases/phase-18-web-clients-waiter-kitchen.md`.

Since Phase 19 the pages live in `src/HotelPOS.Client.Shared` (a Razor class library) with two hosts: the browser
(`HotelPOS.Web`, served at `/app`) and the Android app **HotelPOS Staff** (`src/HotelPOS.Android`, .NET MAUI Blazor
Hybrid, package `com.hotelpos.staff`). The app keeps its refresh token in Android SecureStorage and uses the existing
`/api/auth/login|refresh|logout`; the browser keeps the cookie flow. See `docs/phases/phase-19-android-app.md`.

## 2. Network layout and addressing

- The server PC gets a **static LAN IP** (DHCP reservation on the router or manual IP).
- The API listens on `http://0.0.0.0:5000` (configurable in `appsettings.json` -> `Kestrel:Endpoints`).
  HTTPS (`:5001`) with a self-signed or internal CA certificate is optional and documented in Phase 10.
- PostgreSQL listens on localhost only (`listen_addresses = 'localhost'`). TCP 5432 is **not** opened on the firewall.
- Windows Firewall on the server allows inbound TCP 5000 (and 5001 if HTTPS) from the private network
  profile only.
- Clients store the base URL in their local configuration (`ApiBaseUrl`). Nothing is compiled in.
- Thermal/network printers live on the same LAN (e.g. `192.168.1.50:9100`); printer addresses are
  client configuration too.

Example configuration (`%ProgramData%\HotelPOS\Desktop\settings.json`):

```json
{
  "ApiBaseUrl": "http://192.168.1.100:5000",
  "DeviceName": "WAITER-01",
  "DeviceType": "Waiter",
  "StationId": null,
  "Printers": {}
}
```

## 3. Solution structure and project dependencies

```
HotelPOS.sln
  src/HotelPOS.Contracts        (no dependencies)
  src/HotelPOS.Domain           -> Contracts (enums only)
  src/HotelPOS.Application      -> Domain, Contracts
  src/HotelPOS.Infrastructure   -> Application, Domain
  src/HotelPOS.Api              -> Application, Infrastructure, Contracts
  src/HotelPOS.Desktop          -> Contracts
  tests/HotelPOS.Domain.Tests       -> Domain
  tests/HotelPOS.Application.Tests  -> Application (+ Infrastructure for EF-backed tests)
  tests/HotelPOS.Api.Tests          -> Api (WebApplicationFactory)
  tests/HotelPOS.Desktop.Tests      -> Desktop
```

Shared build settings live in `Directory.Build.props` (nullable enabled, implicit usings, warnings as
errors for `src/`, `LangVersion latest`) and `Directory.Packages.props` (central package versions).

### Folder layout per project

```
HotelPOS.Domain/
  Common/        BaseEntity, IAuditable, IHasRowVersion, DomainException
  Identity/      User, Role, UserRole, RefreshToken, Device
  Floor/         Section, Table
  Menu/          Category, MenuItem, ModifierGroup, ModifierOption, Tax, PreparationStation
  Orders/        Order, OrderItem, OrderItemModifier, OrderStateMachine
  Kitchen/       KitchenOrder, KitchenOrderItem, KitchenOrderStateMachine
  Billing/       Bill, BillItem, Discount, PaymentMethod, Payment, BillCalculator
  Administration/ Setting, AuditLog, IdempotencyRecord   (not "System": that would shadow the .NET namespace)

HotelPOS.Application/
  Common/        Result<T>, AppError, ErrorCodes, PagedQuery, ICurrentUser, IClock, IAppDbContext,
                 IRealtimeNotifier, IAuditService, IIdempotencyStore
  Common/Cqrs/   ICommand, ICommand<T>, IQuery<T>, IRequirePermission, IHasInput<T>, IAuditedCommand,
                 ICommitOnFailure, IAuditTrail, IAfterCommit (Phase 12)
  Common/Behaviors/ Logging, Authorization, Validation, Transaction, Audit (MediatR pipeline)
  Features/<Module>/Commands|Queries/<Name>.cs
                 one file per use case: request record + DTO validator + handler; shared steps of a
                 module in small internal helpers beside them (Auth, Users, Security, Devices, Floor,
                 Menu, Orders, Kitchen, Billing, Print, Reports, Operations)

HotelPOS.Infrastructure/
  Persistence/   AppDbContext, EntityConfigurations/, Migrations/, Interceptors/, Seed/
  Identity/      PasswordHasher adapter, JwtTokenService
  Services/      SystemClock, AuditService, IdempotencyStore, BackupService

HotelPOS.Api/
  Controllers/   one controller per resource (ApiControllerBase turns Result<T> into the envelope)
  Hubs/          RestaurantHub, HubGroups, ConnectionTracker, SignalRRealtimeNotifier (needs the hub type)
  Common/        middleware (correlation id, exceptions, request context), HttpCurrentUser,
                 error-code to status mapping, service registration
  Program.cs, appsettings.json, appsettings.Development.json

HotelPOS.Contracts/
  Common/        ApiResponse<T>, ApiError, PagedResult<T>
  Enums/         OrderStatus, KitchenOrderStatus, TableStatus, PaymentStatus, DeviceType, ...
  Auth/, Users/, Floor/, Menu/, Orders/, Kitchen/, Billing/, Reports/, Admin/, Print/
                 request + response DTOs
  Realtime/      HubEvents (string constants), event payload records
  Security/      Roles, Permissions constants

HotelPOS.Desktop/
  see docs/05-desktop-application.md
```

## 4. Layer responsibilities

| Layer | Does | Does not |
|---|---|---|
| Domain | Entities, enums, invariants, state-machine transition tables, money calculation (`BillCalculator`) | Reference EF Core, HTTP, DI |
| Application | Use cases as commands and queries (`SubmitOrderCommand`) with their handlers, FluentValidation validators, the MediatR pipeline (permission, validation, transaction, audit), notifications via interfaces | Know about controllers or SignalR types |
| Infrastructure | EF Core mapping and migrations, hashing, JWT, SignalR publisher, clock, backup | Contain business rules |
| Api | HTTP surface, auth middleware, hub, DI composition, `appsettings` | Contain business rules |
| Contracts | DTOs and constants shared by API and Desktop | Reference any other project |
| Desktop | Views, view-models, local config, printing, connection handling | Business rules, DB access |

Decision (Phase 12, replacing the plain services of v1): **CQRS with MediatR**. Each use case is a request
record returning `Result`/`Result<T>`, sent by the controller; cross-cutting rules (permission, validation,
one transaction per command, audit, events after commit) live once in the pipeline. See phase-12 § 14.

## 5. API request pipeline (middleware order)

```
Request
  -> CorrelationIdMiddleware      (reads/creates X-Correlation-Id, pushes to Serilog LogContext)
  -> Serilog request logging
  -> ExceptionHandlingMiddleware  (maps exceptions -> ApiResponse + status code)
  -> Rate limiter                 (login endpoint, Phase 9)
  -> Authentication (JWT)         (also reads access_token from query string for the hub)
  -> RequestContextLogging        (adds "user@device" to every log line)
  -> Authorization                (fallback policy: authenticated unless [AllowAnonymous])
  -> IdempotencyMiddleware        (only for routes marked [Idempotent]; Phase 4)
  -> Controller -> ISender.Send(request) -> Logging -> Authorization -> Validation -> Transaction
     -> BusinessDayLock (Phase 13: bill/payment commands of a closed day -> DAY_CLOSED) -> Audit
     -> handler -> EF Core (one transaction per command) -> IRealtimeNotifier (after commit)
Response (always wrapped in ApiResponse<T>)
```

## 6. Cross-cutting concerns

### 6.1 Response envelope

Every endpoint returns:

```json
{ "success": true,  "data": { ... }, "message": null, "errors": [], "correlationId": "..." }
{ "success": false, "data": null, "message": "Order cannot be modified because it is already paid.",
  "errors": [ { "code": "INVALID_STATE_TRANSITION", "field": null, "message": "..." } ],
  "correlationId": "..." }
```

HTTP status codes: 200 OK, 201 Created, 400 validation, 401 unauthenticated, 403 forbidden,
404 not found, 409 conflict (state transition, concurrency, lock, duplicate), 429 rate limited,
500 unexpected. Error codes are listed in `docs/03-api-reference.md`.

### 6.2 Validation

FluentValidation validators for every request DTO. Validation failures return 400 with one `ApiError`
per field; the pipeline's ValidationBehavior runs them. Business-rule failures (state, concurrency, permissions on
data) return 409/403 from the handler via `Result<T>`.

### 6.3 Idempotency

Critical POSTs (`create order`, `append items`, `request bill`, `payment`, `refund`) require an
`Idempotency-Key` header (GUID generated by the client and persisted with the pending action).

- First call: process, store `{key, userId, route, requestHash, statusCode, responseBody, expiresAt}`.
- Retry with same key and same request hash: return the stored response (no re-processing).
- Same key, different payload: 409 `IDEMPOTENCY_KEY_REUSED`.
- Same key while first call still running: 409 `REQUEST_IN_PROGRESS`.
- Keys expire after 48 hours (cleanup job).

### 6.4 Concurrency

`RowVersion` (PostgreSQL system column `xmin`, mapped as a `uint` concurrency token) on `Tables`, `Orders`,
`Bills`, `MenuItems`, `Users`. DTOs carry `rowVersion` (opaque base64). Update requests include it; mismatch => 409 `CONCURRENCY_CONFLICT` with the
current state in `data` so the client can refresh. Bills additionally use a soft **claim lock**
(cashier + device + expiry) so other counters see "being handled by BILLING-01".

### 6.5 Transactions

Every command runs inside one `IDbContextTransaction` (TransactionBehavior; no process-wide lock, PostgreSQL row
locks and row versions decide). SignalR events are queued on `IAfterCommit` and published **after** commit. If publishing fails the
data is still correct; clients resync on reconnect.

### 6.6 Audit

Handlers of `IAuditedCommand`s state `IAuditTrail.Add(action, entityType, entityId, oldValues, newValues)` for critical
operations; AuditBehavior writes them. Entry includes user, device, machine name, IP, correlation id,
timestamp. Written in the same transaction as the change.

### 6.7 Time and money

- All timestamps stored as UTC (`timestamp(3) with time zone`), displayed in the venue's local time by the client.
- `BusinessDayStartTime` setting defines report day boundaries.
- Money: `decimal(18,2)`, rounding `MidpointRounding.AwayFromZero` at line level; optional whole-currency
  round-off on the bill total (`RoundOff` column). Currency symbol from settings.

### 6.8 Logging

Serilog on both sides. API: console + rolling file (`logs/api-.log`, 30-day retention), enriched with
correlation id, user id, device name. Desktop: rolling file in `%LocalAppData%\HotelPOS\logs`.
Never log passwords, tokens, authorization headers or payment references in full.

### 6.9 Configuration

| Where | What |
|---|---|
| API `appsettings.json` | Connection string, JWT (issuer, audience, key, lifetimes), Kestrel URLs, Serilog, `Database:AutoMigrate`, `Backup:*` |
| DB `Settings` table | Restaurant name/address/GSTIN, currency, invoice prefix, round-off, business day start, kitchen thresholds, receipt footer, auto-print flags |
| Desktop `settings.json` | `ApiBaseUrl`, `DeviceName`, `DeviceType`, `StationId`, printers, theme |

Secrets (JWT signing key, DB password) are never in the desktop and never committed; the server
installer generates the JWT key.

## 7. Real-time (summary)

One hub at `/hubs/restaurant`. Clients join groups by role, station and user. Events carry ids and a
small display payload; clients fetch details from REST when needed and resync on reconnect.
Details: `docs/04-realtime-events.md`.

## 8. Desktop (summary)

Single WPF executable, MVVM with CommunityToolkit, DI via generic host, role-based shell, first-run
configuration, DPAPI-protected refresh token, connection manager with status indicator, printing behind
`IPrintService`. Details: `docs/05-desktop-application.md`.

## 9. Key decisions (ADR summary)

| # | Decision | Why |
|---|---|---|
| 1 | Enums live in `HotelPOS.Contracts`; Domain references Contracts for enums only | Avoids duplicating enums between server and desktop; Contracts has zero dependencies |
| 2 | Plain application services + `Result<T>` in v1; commands and queries through MediatR 12 since Phase 12 | One shape per use case and cross-cutting rules in one pipeline once the feature count grew |
| 3 | `int` identity primary keys | Readable, fast, sufficient for a single venue; multi-branch later would use a branch id or separate databases |
| 4 | Order numbers from a SQL `SEQUENCE`; invoice numbers from a locked counter row assigned at finalisation | Order numbers may have gaps; invoice numbers must be gap-free |
| 5 | Table status is **stored** and updated by the server, not computed on read | Simple map queries and events; derivation rules live in one place |
| 6 | Kitchen progress tracked per ticket; order status derived from its tickets | Supports multiple stations and appended batches |
| 7 | Thin SignalR events + REST fetch | Server stays the source of truth; events can be lost safely |
| 8 | Printing executed on the client PC behind `IPrintService`, with print documents generated by the API | Printers are attached to client PCs; formatting data (GSTIN, tax breakup) is authoritative on server |
| 9 | API hosted as a Windows Service, auto-migrate on startup (configurable) | Zero-touch restarts on the venue's server |
| 10 | Role-based authorization in v1 with permission constants; editable role permissions in Phase 9 | Ship fast, refine later without changing endpoints |
| 11 | Offline licence: ECDSA P-256 key bound to the server's machine id, public key embedded, private key only in the vendor tool (`tools/HotelPOS.LicenseGenerator`); expiry makes the server read-only (Phase 16) | No licence server or internet needed; a lapsed licence never locks the restaurant out of its own data |
| 12 | Terminals update from the server: it hosts the client installer under `/updates/`, the desktop downloads, checks SHA-256 and runs it (Phase 16) | Works on the LAN with the existing Phase 10 version gate; no third-party updater or internet |

## 10. Extension points

- **Mobile/web clients**: the API and SignalR hub are client-agnostic; Contracts can be published as a
  NuGet package.
- **Multiple kitchens/stations**: already modelled (`PreparationStation`, ticket per station).
- **Multiple counters**: already modelled (device identity, bill claim locks, idempotent payments).
- **Offline queue**: `IOrderSubmitter` on the desktop can be replaced by an outbox implementation.
- **Multiple branches**: run one API + DB per branch, or add `BranchId` to floor/menu/orders later.
