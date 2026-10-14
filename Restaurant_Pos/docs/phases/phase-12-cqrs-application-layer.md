# Phase 12 — CQRS Application Layer (MediatR)

Status: In progress (started 2026-10-14) · Depends on: Phase 11 (independent in code; scheduled after it) · Reference: BillingHub `Core/CQRS`,
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

Server side only for now: the desktop `ViewModelBase` / `ExecuteAsync` work (§ 3, § 4 Desktop row) is **deferred**
because Phase 11 is reworking the desktop at the same time; it will be done once both branches are merged.

**12.1 Primitives and pipeline (2026-10-14)**

- MediatR **12.5.0** (pinned in `Directory.Packages.props`; 13+ needs a commercial licence). No known
  vulnerabilities (`dotnet list package --vulnerable --include-transitive`).
- `Application/Common/Cqrs`: `ICommand`, `ICommand<T>` (both `ICommandBase`), `IQuery<T>`, their handler
  interfaces, all returning the existing `Result` / `Result<T>`. `IFailureResult<TSelf>` (a static abstract
  `Failure(AppError)` on `Result` and `Result<T>`) lets behaviours build a failure of the handler's own result type
  without reflection.
- Pipeline (`Application/Common/Behaviors`, registration order = execution order):
  1. `LoggingBehavior`: request name, user, duration and outcome code at Debug; Warning above 500 ms.
  2. `AuthorizationBehavior`: `IRequirePermission.Permission` checked with `ICurrentUser.HasPermission`; denial is
     `Result` failure `FORBIDDEN` with the endpoint policy's message. Each request names the same permission as its
     endpoint policy, so the API's answers do not change.
  3. `ValidationBehavior`: FluentValidation, failure = `VALIDATION_ERROR`, "One or more fields are invalid.", one
     error per field with camel-cased names, exactly as `ValidationFilter` answered. Requests wrap their Contracts
     DTO (`IHasInput<TDto>`) and are validated by the DTO's existing validator (`InputValidator`, registered per
     request at start-up), so field names stay `name`, `items[0].quantity`, ...
  4. `TransactionBehavior`: commands only; one `BeginTransaction` per command, commit on success, rollback (and a
     cleared change tracker) on failure or exception. No process-wide lock: PostgreSQL row locks and the `xmin`
     row versions handle terminals working at once. A command marked `ICommitOnFailure` commits its failure (used
     where the failure itself must be kept, e.g. a counted failed login). EF Core's automatic savepoints keep the
     transaction usable after a caught `SaveChanges` failure (concurrency conflicts, unique violations).
  5. `AuditBehavior`: commands declared `IAuditedCommand` state their entries on the scoped `IAuditTrail` (action
     constant from `AuditActions`, entity, id, old/new values); the behaviour writes them inside the transaction
     after the handler succeeded. A command that records entries without declaring `IAuditedCommand` fails.
- After-commit step: handlers queue real-time events and other observable side effects (closing hub connections,
  deleting files) on the scoped `IAfterCommit`; `TransactionBehavior` runs them after the commit and drops them on
  rollback. A failing step is logged and does not fail the request (docs/04 § 4 rule 2 applied to every step).
- Tests (`Cqrs/PipelineBehaviorTests`, probe commands registered only in the test fixture): authorization denial,
  validation shape, commit with audit row and user, event published only once other connections can read the
  change, rollback on failure and on exception (no state, no audit row, no event), no leak of rolled-back changes
  into the next command of the same scope, `ICommitOnFailure`, undeclared audit entries.
