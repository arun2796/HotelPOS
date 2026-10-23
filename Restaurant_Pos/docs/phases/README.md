# Phase Roadmap

Last updated: 2026-10-20

Each phase ends with a working, demonstrable system increment. Later phases only add; they never
require rewriting earlier ones. Reference documents (`docs/00`–`06`) describe the target design; phase
documents say what to build, in which order, and how to verify it.

## Roadmap

| # | Phase | Depends on | Outcome at the end of the phase | Status |
|---|---|---|---|---|
| 0 | [Planning](../00-project-overview.md) | — | This documentation set | Done |
| 1 | [Foundation](phase-01-foundation.md) | 0 | Solution, database, API skeleton, JWT login, users admin, SignalR hub, WPF shell with first-run config and connection indicator | Done (2026-10-07) |
| 2 | [Tables & Sections](phase-02-tables-and-sections.md) | 1 | Admin manages floor; waiter sees a live table map; occupy/release with concurrency control | Done (2026-10-07) |
| 3 | [Menu & Pricing](phase-03-menu-and-pricing.md) | 1 | Categories, items, modifiers, taxes, stations; compact cached menu for clients | Done (2026-10-07) |
| 4 | [Waiter Ordering](phase-04-waiter-ordering.md) | 2, 3 | Draft -> submit -> append -> cancel with idempotency; order builder UI; local drafts | Done (2026-10-07) |
| 5 | [Kitchen Display](phase-05-kitchen-display.md) | 4 | Tickets per station, kitchen workflow, waiter "order ready" notifications, serve | Done (2026-10-07) |
| 6 | [Billing & Payments](phase-06-billing-and-payments.md) | 5 | Bill request queue, discounts, tax, finalize/invoice, split payments, multi-counter safety, void/refund | Done (2026-10-07) |
| 7 | [Printing](phase-07-printing.md) | 5, 6 | KOT, invoice, receipt via thermal/Windows/network printers behind `IPrintService` | Done (2026-10-07) |
| 8 | [Reports & Dashboard](phase-08-reports-and-dashboard.md) | 6 | Sales, items, payments, cancellations, discounts, taxes, staff and kitchen performance; dashboard | Done (2026-10-08) |
| 9 | [Security & Audit](phase-09-security-and-audit.md) | 1–6 | Editable permissions, lockout, rate limiting, device management, audit viewer, hardening | Code complete (§ 15 server items closed 2026-10-15; two-machine demo open) |
| 10 | [Production Readiness](phase-10-production-readiness.md) | all | Windows Service hosting, installers, backup/recovery, monitoring, LAN/firewall docs, go-live checklist | Code complete (installers 0.1.0 built 2026-10-19; clean-VM install test `docs/ops/10` and venue load test open) |
| 11 | [UI Design System](phase-11-ui-design-system.md) | 10 | BillingHub look and shell: tokens, MaterialDesign base, Mica, dark mode, command palette, hubs, KPI/report controls, charts, touch density | Code complete (manual demo open) |
| 12 | [CQRS Application Layer](phase-12-cqrs-application-layer.md) | 11 | MediatR commands/queries with logging, permission, validation, transaction and audit behaviours; thin controllers | Done (2026-10-14) |
| 13 | [Shifts, Cash & Day End](phase-13-shifts-cash-and-day-end.md) | 12 | Cashier shifts, float and denominations, pay-in/out, X/Z reports, Day End with lock | Code complete (manual demo open) |
| 14 | [Templates, PDF & Excel](phase-14-templates-pdf-excel.md) | 12 | One render model, Template Studio, PDF and Excel exports, print log with DUPLICATE | Code complete (manual demo open) |
| 15 | [GST Reports & Period Close](phase-15-gst-reports-and-period-close.md) | 13, 14 | GSTR-1 JSON, GSTR-3B, HSN/SAC summary, B2B GSTIN, month/year lock | Code complete (GSTR-1 offline-tool check and manual demo open) |
| 16 | [Licensing, Updates & Import](phase-16-licensing-updates-import.md) | 10 | Machine-locked licence and trial, LAN auto-update, menu/customer import | Code complete (manual demo open) |
| 17 | [Enterprise UI Refresh](phase-17-enterprise-ui-refresh.md) | 11, 16 | Light green enterprise look on every screen and dialog; presentation only, behaviour unchanged | Code complete (manual demo open) |
| 18 | [Web Clients for Waiter and Kitchen](phase-18-web-clients-waiter-kitchen.md) | 9, 12, 16, 17 | Blazor PWA served by the API under /app: waiter on phones, kitchen on tablets; same backend, no new business logic | Code complete (device demo open) |
| 19 | [Android App for Waiter and Kitchen](phase-19-android-app.md) | 18 | .NET MAUI Blazor Hybrid host for the same Razor pages; signed APK, server page, SecureStorage tokens | Code complete (device demo open) |
| 20 | [Enterprise Product Transformation](phase-20-enterprise-transformation.md) | 11–19 | Role-aware notifications on every client, surfaced live events, role homes and dashboard panels, web polish, HTTPS by default; audit of the owner's master prompt | Code complete 2026-10-16 (device demo § 11 open; D1–D4 deferred by owner) |
| 21 | [Role-based UI/UX Redesign](phase-21-role-ui-ux-redesign.md) | 17–20 | Waiter, kitchen, cashier and admin clients restyled to the owner's mock-ups (`docs/design/reference`) on one design system; presentation only | Code complete (merged to main 2026-10-17; D1–D11 § 4; galleries regenerated; device demo open) |
| 22 | [Features excluded from Phase 21](phase-22-excluded-features.md) | 21 | Order types, held orders, reservations, wallet, item-level bill split, hourly sales, expenses + P&L, floor-plan geometry, staff on duty, ticket pictures, KOT PDF from the browser | Done except device demo (merged to main 2026-10-19; full suite 1,579 passed; owner signed off E1–E11 and dropped item split B3/B4; migrations rehearsed on a DB copy; release notes `phase-22-release-notes.md`) |
| 25 | [Admin UI polish](phase-25-admin-ui-polish.md) | 21, 24 | Enterprise finish for the admin/manager screens: sidebar order and icons, section labels, status badges in tables; pass 2 (page headers, form and table systems) proposed | Done 2026-10-23 (passes 1, 2a, 2b; renders in `docs/ui-ux/desktop/admin`; three items left by decision, see § Left out) |
| 24 | [.NET 10 LTS](phase-24-dotnet-10.md) | 1–23 | Every project on .NET 10 (SDK 10.0.300, EF Core 10, ASP.NET Core 10, MAUI 10, Android API 36); publish gate at 10.0.8; .NET 8 leaves support 2026-11-10 | Code complete 2026-10-22 on branch `phase-24-dotnet-10` (clean-VM test and phone check open) |
| 23 | [Go-live hardening](phase-23-go-live-hardening.md) | 1–22 | The must-fix findings of the 2026-10-22 production-readiness review: no duplicate order on a commit-time timeout, no double partial payment, service start-up inside the SCM window with a pre-upgrade backup, no placeholder JWT key, no default admin password (enforced must-change), signed update manifests, Android publish script | Code complete 2026-10-22 (items 1–7; .NET 10 move deferred to its own task; clean-VM test still open) |

Phases 2 and 3 are independent and can be built in parallel. Phase 7 can start once Phase 5 exists
(KOT) and finish after Phase 6 (invoice/receipt). Phase 8 needs settled bills (Phase 6).

Phases 11–16 (decided 2026-10-14) bring in the design, pattern and features of BillingHub, the owner's GST
billing application, while keeping HotelPOS's multi-terminal server architecture: BillingHub runs on one PC with
a local database, which cannot serve waiter tablets, kitchen screens and billing counters at the same time.

```
 1 ──┬── 2 ──┐
     │       ├── 4 ── 5 ── 6 ──┬── 7
     └── 3 ──┘                 ├── 8
                               └── 9 ── 10 ──┬── 11 ── 12 ──┬── 13 ──┐
                                             │              └── 14 ──┴── 15
                                             └── 16
```

## Phase document template

Every phase document has the same sections:

1. **Goal** — one paragraph.
2. **Prerequisites** — phases and decisions it relies on.
3. **Scope** — In / Out.
4. **Deliverables by project** — Domain, Application, Infrastructure, Api, Contracts, Desktop, Tests.
5. **Data model** — new/changed entities and the migration name.
6. **API endpoints** — with roles.
7. **Real-time events**.
8. **Business rules** — the rules the server enforces.
9. **Desktop screens** — what the user sees and does.
10. **Tests** — required automated tests.
11. **Manual demo script** — steps to verify on two machines.
12. **Acceptance criteria** — checklist for Done.
13. **Risks and notes**.
14. **Changes during implementation** — filled while building.

## Suggested build order inside a phase

Contracts (DTOs, enums, events) -> Domain (entities, rules, tests) -> Infrastructure (EF config,
migration, seed) -> Application (services, validators, tests) -> Api (controllers, hub events,
integration tests) -> Desktop (services, view-models, views, VM tests) -> docs update -> demo script.

## Tracking

Status values: Not started / In progress / Blocked / Done. Update the table above and the one in the
root `README.md` whenever a phase changes state.
