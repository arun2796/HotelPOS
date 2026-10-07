# HotelPOS — Hotel / Restaurant POS & Order Management

Windows desktop POS for hotels and restaurants, running entirely on the venue's own Wi-Fi / LAN.

Waiter, Kitchen, Billing and Admin modules live in one WPF application. The desktop never touches the
database. Everything goes through a central ASP.NET Core Web API, SignalR pushes real-time updates, and
PostgreSQL is the single source of truth.

```
   WAITER WPF        KITCHEN WPF        BILLING WPF        ADMIN WPF
       |                 |                  |                 |
       +-----------------+------------------+-----------------+
                              HTTP + SignalR  (LAN)
                                      |
                                      v
                     ASP.NET CORE WEB API  --  SignalR Hub
                                      |
                                      v
                                 POSTGRESQL
```

## Project status

| Item                               | State        |
|------------------------------------|--------------|
| Planning documents                 | Done         |
| Phase 1 – Foundation               | Done         |
| Phase 2 – Tables & Sections        | Done         |
| Phase 3 – Menu & Pricing           | Done         |
| Phase 4 – Waiter Ordering          | Done         |
| Phase 5 – Kitchen Display          | Not started  |
| Phase 6 – Billing & Payments       | Not started  |
| Phase 7 – Printing                 | Not started  |
| Phase 8 – Reports & Dashboard      | Not started  |
| Phase 9 – Security & Audit         | Not started  |
| Phase 10 – Production Readiness    | Not started  |

Update this table (and `docs/phases/README.md`) when a phase starts or finishes.

## Quick start (development)

```powershell
dotnet tool restore
dotnet test HotelPOS.sln                       # builds everything and runs the tests (local PostgreSQL required)
dotnet run --project src/HotelPOS.Api          # API + SignalR on http://localhost:5000 (Swagger at /swagger)
dotnet run --project src/HotelPOS.Desktop      # WPF client; first run asks for the server address
```

Development sign-in: `admin` / `Admin@123` (must change it), or `manager1`, `waiter1`, `kitchen1`,
`cashier1` with `Pass@123`. Details: [docs/dev-setup.md](docs/dev-setup.md).

## Where to start

1. [Project overview](docs/00-project-overview.md) — what we are building, for whom, and the rules we never break.
2. [Architecture](docs/01-architecture.md) — system, network, solution layout, cross-cutting concerns.
3. [Phase roadmap](docs/phases/README.md) — the build order, then each phase document.

## Documentation map

| Document | Purpose |
|---|---|
| [docs/00-project-overview.md](docs/00-project-overview.md) | Objective, roles, scope, stack, golden rules, glossary |
| [docs/01-architecture.md](docs/01-architecture.md) | Architecture, network, projects, pipeline, key decisions |
| [docs/02-domain-and-database.md](docs/02-domain-and-database.md) | Entities, enums, state machines, numbering, money rules |
| [docs/03-api-reference.md](docs/03-api-reference.md) | Endpoint catalogue, response envelope, error codes, protocols |
| [docs/04-realtime-events.md](docs/04-realtime-events.md) | SignalR hub, groups, event catalogue, reconnection & resync |
| [docs/05-desktop-application.md](docs/05-desktop-application.md) | WPF structure, MVVM rules, configuration, modules, UX rules |
| [docs/06-conventions-and-dod.md](docs/06-conventions-and-dod.md) | Coding/naming conventions, testing, Definition of Done |
| [docs/dev-setup.md](docs/dev-setup.md) | Build, run and test on a development PC |
| [docs/phases/README.md](docs/phases/README.md) | Roadmap and phase index |
| [docs/phases/phase-01-foundation.md](docs/phases/phase-01-foundation.md) … [phase-10](docs/phases/phase-10-production-readiness.md) | One document per phase |

## Golden rules (short form)

1. WPF never connects to the database. Clients talk only to the API.
2. The API/database is the source of truth. SignalR is a notification, not a state store.
3. No hard-coded server IP, printer, restaurant name or tax config. Everything is configuration.
4. Network drops must never crash the app, lose an order, or duplicate one. Idempotency keys on critical calls.
5. Payment, billing and order submission run inside database transactions.
6. Server-side validation and role-based authorization on every endpoint.
7. Every critical operation is audited.
8. Keep Waiter, Kitchen and Billing screens simple and fast. Do not over-engineer v1.

Full list: [docs/00-project-overview.md § Non-negotiable rules](docs/00-project-overview.md).
