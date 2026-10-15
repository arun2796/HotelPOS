# 00 — Project Overview

Last updated: 2026-10-07

## 1. Objective

Build a production-ready Windows desktop Hotel/Restaurant POS and Order Management System that runs on
the venue's internal Wi-Fi/LAN. A waiter takes an order at a table, the kitchen receives it instantly,
updates preparation status, and the completed order becomes available at the billing counter. All
terminals stay synchronised in real time through one central server.

The system is designed for real daily restaurant operation (speed, reliability, auditability), not as a
CRUD demonstration.

## 2. End-to-end flow

```
WAITER                 KITCHEN                WAITER               BILLING              TABLE
Login                  Receive ticket         "Order Ready"        Receive bill
Select table     -->   Accept           -->   Serve          -->   Review / discount -->  Available
Build order            Preparing              Request bill         Payment(s)             again
Send to kitchen        Ready                                       Invoice / receipt
                                                                   Close order
```

Every arrow above is an API call that persists state in PostgreSQL and then publishes a SignalR event
so the other terminals refresh.

## 3. Users, roles and modules

| Role    | Primary device     | Modules visible after login |
|---------|--------------------|-----------------------------|
| Waiter  | Waiter PC / tablet | Dashboard (mini), Tables (map), Orders (builder), My Orders |
| Kitchen | Kitchen display PC | Kitchen Display (New / Preparing / Ready), Completed Orders |
| Cashier | Billing counter PC | Billing queue, Bill detail & Payments, Receipts, Closed Bills |
| Manager | Any                | Everything a Waiter/Cashier sees + Reports + Menu/Tables maintenance + approvals (discounts, voids, cancellations) |
| Admin   | Any                | Dashboard, Users, Roles, Tables, Menu, Categories, Taxes, Discounts, Payment Methods, Devices, Reports, Audit Log, Settings, Backup |

One WPF executable serves all roles. The shell shows only the modules permitted for the logged-in user.
Every machine also has a **device identity** (e.g. `WAITER-01`, `KITCHEN-01`, `BILLING-02`) stored in
its local configuration, independent of who logs in.

## 4. Scope

### Version 1 (phases 1-10)

- Authentication (JWT), role-based authorization, users/roles administration
- Sections and tables with live status map
- Menu: categories, items, modifiers, taxes, preparation stations, availability
- Waiter ordering: draft, send to kitchen, append items, cancel, notes/modifiers
- Kitchen display with station routing and status workflow
- Billing: bill request queue, discounts, tax calculation, split payments (Cash/Card/UPI), invoice
  numbering, void/refund with manager approval, multi-counter safety
- Printing abstraction: KOT, invoice, receipt (thermal / Windows / network printers)
- Reports and dashboard
- Audit log of every critical operation
- Connection resilience: automatic reconnect, state resync, idempotent retries
- Installers, LAN/firewall setup, backup and recovery, logging

### Deferred (designed for, not built in v1)

- Offline order queue with background synchronisation (v1 is online/LAN only; drafts are kept locally so
  typed items are never lost)
- Multiple branches, mobile or web clients (the API is already client-agnostic)
- Inventory / stock, reservations, loyalty, online ordering integrations
- Tax-inclusive pricing (schema has the flag; calculation supports exclusive pricing only in v1)

## 5. Technology stack

| Layer     | Choice |
|-----------|--------|
| Desktop   | C#, .NET 8, WPF, MVVM with `CommunityToolkit.Mvvm`, `Microsoft.Extensions.Hosting` for DI, Serilog, `Microsoft.AspNetCore.SignalR.Client` |
| Backend   | ASP.NET Core 8 Web API, EF Core 8 + PostgreSQL (Npgsql), SignalR, JWT bearer auth, FluentValidation, Serilog |
| Database  | PostgreSQL 16+ (free; one server per venue, on the API machine) |
| Tests     | xUnit, FluentAssertions, NSubstitute, `WebApplicationFactory`, local PostgreSQL / Testcontainers |
| Packaging | `dotnet publish` self-contained, Inno Setup installers, API hosted as a Windows Service |

Solution layout (`HotelPOS.sln`):

```
src/
  HotelPOS.Domain          entities, enums, state machines, domain rules
  HotelPOS.Application     use cases / application services, validation, interfaces
  HotelPOS.Infrastructure  EF Core, migrations, identity hashing, clock, SignalR notifier
  HotelPOS.Api             controllers, hub, middleware, composition root
  HotelPOS.Contracts       DTOs, enums, event names, role/permission constants (shared with desktop)
  HotelPOS.Desktop         WPF client (all role modules)
tests/
  HotelPOS.Domain.Tests
  HotelPOS.Application.Tests
  HotelPOS.Api.Tests
  HotelPOS.Desktop.Tests   (view-model tests, added from Phase 4)
```

## 6. Non-negotiable rules

1. Do not connect WPF directly to the database. Database credentials never leave the server.
2. All clients communicate with the ASP.NET Core API only.
3. SignalR is used for real-time notification; the API/database remains the source of truth. A client
   that receives an event fetches the authoritative state from the API when it needs details.
4. All business rules, validation, calculations, authorization and audit happen in the backend.
5. Never hard-code the server IP, API URL, connection string, printer, restaurant name or taxes.
6. The desktop must survive Wi-Fi interruptions: show Connected / Reconnecting / Disconnected,
   reconnect automatically, and resync state afterwards. It must never crash on network loss.
7. Never lose or duplicate an order or payment. Critical POSTs carry an `Idempotency-Key`. The UI never
   claims success unless the API returned success.
8. Payment, bill generation and order submission are atomic database transactions.
9. Optimistic concurrency (`RowVersion`) on orders, tables, bills and menu items. The server is
   authoritative when two terminals conflict.
10. Role-based authorization on every endpoint. Waiters cannot reach admin settings, kitchen cannot
    touch payments, cashiers cannot change prices.
11. Strongly typed enums and explicit state machines. Invalid transitions (e.g. Paid -> Preparing) are
    rejected by the server.
12. Every critical operation (order change/cancel, price change, discount, payment, refund, void) writes
    an audit log entry with user, device, old/new values and correlation id.
13. Passwords are hashed (PBKDF2 via ASP.NET Core `PasswordHasher`). Tokens, passwords and payment
    secrets are never logged.
14. Keep Waiter, Kitchen and Billing UIs simple and fast; prioritise reliability over visual effects.
15. Do not over-engineer v1, but keep the structure open for more branches, kitchens, counters and
    mobile/web clients.
16. The system must keep working when the internet is down as long as the LAN and server are up.
17. Provide a first-run configuration screen for the API server address and device identity.
18. Use DTOs on the API boundary; never expose EF entities.
19. Use database transactions, indexes, foreign keys and constraints; avoid duplicate data (snapshot
    only what must be immutable, such as prices on order lines).
20. Document setup (PostgreSQL, migrations, firewall, static IP, client configuration, backup, recovery).

## 7. Glossary

| Term | Meaning |
|---|---|
| **Order** | What a table ordered during one sitting. Has many items, one waiter, one table. |
| **Batch** | A group of items sent to the kitchen together. The first batch is the initial submit; later batches are appended items. |
| **KOT / Kitchen ticket** | `KitchenOrder` row: one batch routed to one preparation station. An order with items for Main Kitchen and Bar in one batch produces two tickets. |
| **Preparation station** | Main Kitchen, Bar, Bakery, Beverage... Each menu item belongs to one station. |
| **Cover / Guest count** | Number of guests on the order. |
| **Bill** | Financial document generated when the waiter requests the bill. Holds snapshotted lines, discount, tax, totals, payments. |
| **Invoice number** | Gap-free sequential number assigned when a bill is finalised. |
| **Business day** | Reporting day that starts at a configurable time (e.g. 04:00) so late-night sales count toward the right day. |
| **Device** | A registered client installation (`WAITER-01`), independent of the logged-in user. |
| **Idempotency key** | Client-generated GUID sent with critical POSTs so a retry cannot create a duplicate. |

## 8. Success criteria for v1

- A waiter can log in, pick a table and send an order to the kitchen in under 30 seconds with no
  unnecessary dialogs.
- A kitchen ticket appears on the kitchen display within 1 second of "Send to Kitchen" on the LAN.
- Pulling the Wi-Fi cable mid-operation never crashes a client; the client reconnects and resyncs
  without restarting, and the user always knows whether the order was saved.
- Two cashiers cannot settle the same bill twice; two waiters cannot double-open the same table.
- Every payment, discount, refund, void and cancellation is visible in the audit log.
- A fresh server can be set up from the installation guide by a technician without reading source code.
