# Phase Roadmap

Last updated: 2026-10-07

Each phase ends with a working, demonstrable system increment. Later phases only add; they never
require rewriting earlier ones. Reference documents (`docs/00`–`06`) describe the target design; phase
documents say what to build, in which order, and how to verify it.

## Roadmap

| # | Phase | Depends on | Outcome at the end of the phase | Status |
|---|---|---|---|---|
| 0 | [Planning](../00-project-overview.md) | — | This documentation set | Done |
| 1 | [Foundation](phase-01-foundation.md) | 0 | Solution, database, API skeleton, JWT login, users admin, SignalR hub, WPF shell with first-run config and connection indicator | Done (2026-10-07) |
| 2 | [Tables & Sections](phase-02-tables-and-sections.md) | 1 | Admin manages floor; waiter sees a live table map; occupy/release with concurrency control | Not started |
| 3 | [Menu & Pricing](phase-03-menu-and-pricing.md) | 1 | Categories, items, modifiers, taxes, stations; compact cached menu for clients | Not started |
| 4 | [Waiter Ordering](phase-04-waiter-ordering.md) | 2, 3 | Draft -> submit -> append -> cancel with idempotency; order builder UI; local drafts | Not started |
| 5 | [Kitchen Display](phase-05-kitchen-display.md) | 4 | Tickets per station, kitchen workflow, waiter "order ready" notifications, serve | Not started |
| 6 | [Billing & Payments](phase-06-billing-and-payments.md) | 5 | Bill request queue, discounts, tax, finalize/invoice, split payments, multi-counter safety, void/refund | Not started |
| 7 | [Printing](phase-07-printing.md) | 5, 6 | KOT, invoice, receipt via thermal/Windows/network printers behind `IPrintService` | Not started |
| 8 | [Reports & Dashboard](phase-08-reports-and-dashboard.md) | 6 | Sales, items, payments, cancellations, discounts, taxes, staff and kitchen performance; dashboard | Not started |
| 9 | [Security & Audit](phase-09-security-and-audit.md) | 1–6 | Editable permissions, lockout, rate limiting, device management, audit viewer, hardening | Not started |
| 10 | [Production Readiness](phase-10-production-readiness.md) | all | Windows Service hosting, installers, backup/recovery, monitoring, LAN/firewall docs, go-live checklist | Not started |

Phases 2 and 3 are independent and can be built in parallel. Phase 7 can start once Phase 5 exists
(KOT) and finish after Phase 6 (invoice/receipt). Phase 8 needs settled bills (Phase 6).

```
 1 ──┬── 2 ──┐
     │       ├── 4 ── 5 ── 6 ──┬── 7
     └── 3 ──┘                 ├── 8
                               └── 9 ── 10
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
