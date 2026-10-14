# Phase 12 — CQRS Application Layer (MediatR)

Status: Not started · Depends on: Phase 11 (independent in code; scheduled after it) · Reference: BillingHub `Core/CQRS`,
`Core/Features`, `Infrastructure/Behaviors`

## 1. Goal

Restructure the server's Application layer in BillingHub's CQRS style — one file per command or query (request
record, validator, handler) dispatched through MediatR with a pipeline of behaviours — so every use case looks the
same, cross-cutting rules live in one place, and Phases 13–16 are written in this style from the start. The
multi-terminal architecture (API, PostgreSQL, SignalR) does not change.

## 2. Prerequisites

- Phases 1–10. The authorization matrix test, the audit completeness test and the existing service tests are the
  safety net: they must pass unchanged in behaviour after each module moves.

## 3. Scope

**In**
- MediatR 12; `ICommand`, `ICommand<T>`, `IQuery<T>` returning the existing `Result`/`Result<T>`.
- Layout `Application/Features/{Module}/Commands|Queries/{Name}.cs`.
- Pipeline (registration order = execution order):
  1. **Logging** — request name, user, duration; warning above 500 ms.
  2. **Authorization** — `IRequirePermission` on the request (`Permissions.X`); denial is a `Result` failure, no
     exception. Endpoint policies stay as the outer gate.
  3. **Validation** — FluentValidation; returns `Result` failure (BillingHub throws; we do not).
  4. **Transaction** — commands run in one EF Core transaction; no process-wide lock (PostgreSQL and row versions
     handle concurrency between terminals).
  5. **Audit** — commands declare what to audit (`IAuditedCommand` with action, entity, values) instead of the
     action being derived from the class name.
- Controllers become thin (`Send(request)` + envelope). Existing services are dissolved module by module: Floor,
  Menu, Orders, Kitchen, Billing, Print, Reports, Security, Operations.
- Desktop: `ViewModelBase` with `IsBusy`, `ErrorMessage`, `ExecuteAsync` (one error-handling path; no `MessageBox`).

**Out**
- Behaviour changes. API contract changes (routes, DTOs and error codes stay identical).

## 4. Deliverables by project

| Project | Deliverables |
|---|---|
| Application | CQRS primitives, behaviours, `Features/*` replacing `*Service` classes, validators beside their requests |
| Api | Thin controllers |
| Desktop | `ViewModelBase`, view-models migrated to `ExecuteAsync` |
| Tests | Behaviour tests (authorization denial, validation failure, transaction rollback, audit row); existing suites green |

## 5–7. Data model, API endpoints, real-time events

No changes.

## 8. Business rules

- A command either completes and is audited, or leaves no trace (transaction covers state and audit row).
- Real-time events are published after commit (rule from `docs/04` § 4), by a post-commit notification step.

## 10. Tests

All existing tests green after each module; new behaviour tests; matrix snapshot unchanged.

## 12. Acceptance criteria

- [ ] No `*Service` class left for business use cases; every endpoint dispatches a request.
- [ ] Pipeline behaviours tested; matrix and audit completeness tests unchanged and green.
- [ ] Definition of Done satisfied.

## 13. Risks and notes

- Large mechanical change: one module per commit, full suite between modules.

## 14. Changes during implementation

(fill in while building)

### Desktop: ViewModelBase

- `Modules/Common/ViewModelBase` holds `IsBusy`, `ErrorMessage`, `OnBusyChanged()` (where a screen refreshes the
  commands that depend on `IsBusy`), `ExecuteAsync` and `FetchAsync`. 30 screen view-models derive from it; editor
  and dialog view-models (section/table/category/item/user editors, modifier picker, ticket cards) do not.
- `ExecuteAsync` sets `IsBusy`, clears `ErrorMessage` and always resets `IsBusy`. An unexpected exception
  (not a cancellation) is logged and shown as "Something went wrong. The problem was logged; please try again." on
  that screen instead of reaching `App.OnDispatcherUnhandledException`.
- `FetchAsync` (named so it does not read as a call to the screens' own `LoadAsync()`) returns the data or `null`
  with `ErrorMessage` set: the client's own connection text on a connection failure (never "The change was NOT
  saved", which `ApiFailures.Describe` uses for saves), otherwise `ApiFailures.Describe`. It keeps the messages the
  screens showed before; billing setup no longer says "NOT saved" when its load fails.
- Left on their own flow (base class only): payments, discount approval and bill actions in `BillDetailViewModel`;
  the dashboard, closed-bills and order-list refreshes, which keep the last error on screen until a refresh succeeds;
  terminal configuration, whose view has no error banner.
