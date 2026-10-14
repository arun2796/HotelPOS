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

**Conventions used by every module**

- Requests are records named `<Verb><Thing>Command` / `<Verb><Thing>Query`, one file per use case with the
  record, the DTO validator (moved unchanged from `Application/Validation`) and the `internal` handler.
- A request wraps the Contracts DTO as `Input` plus route values (`UpdateTableCommand(int Id, UpdateTableRequest
  Input)`); DTOs and validators did not change, so neither did the JSON contract or validation messages.
- Each request names the permission of its endpoint policy; checks the controllers used to make before calling a
  service (e.g. "only managers can list inactive tables") moved into the handler, after validation, as before.
- Controllers keep their routes, attributes, status codes and messages and call
  `FromResult(await Sender.Send(request, ct), ...)`; `ApiControllerBase.Sender` resolves MediatR's `ISender`.
- Logic shared by several handlers of a module lives in a small `internal` helper beside them (`FloorTables`,
  `FloorSections`), not in a service facade.
- Ported tests send requests through `ISender` (`DatabaseTestBase.SendAsync`, one scope per request like one HTTP
  request) and keep their assertions. Where a test fed a service input that the DTO validator rejects (the API
  always answered 400 for it), the input was made valid so the test still reaches the rule it is about.
- Old services that are not migrated yet keep publishing and auditing directly; shared helpers such as
  `TableEvents` stay "publish now" and migrated handlers queue the call on `IAfterCommit`.

**12.2 Floor (2026-10-14)**

- `SectionService` and `TableService` dissolved into `Features/Floor`: queries `ListSections`, `GetSection`,
  `GetTableMap`, `GetTable`; commands `CreateSection`, `UpdateSection`, `DeactivateSection`, `CreateTable`,
  `UpdateTable`, `DeactivateTable`, `OccupyTable`, `ReleaseTable`, `SetTableServiceState` (out of service / back in
  service). `TableEvents` and `TableService.ToDto` (now `TableMapping`) moved to `Features/Floor` for the order and
  billing services that still use them.
- The explicit transactions of create section / create table are gone (the pipeline's transaction covers the
  second save that used to need them). Table status events go out after the commit as before.
- `FloorServiceTests` → `Floor/FloorTests` (signed in as admin; the duplicate-code test sends `t01` instead of
  `" t01 "`, which the validator rejects).

**12.3 Menu (2026-10-14)**

- `CategoryService`, `MenuItemService`, `ModifierService`, `StationService`, `TaxService` and `MenuQuery` dissolved
  into `Features/Menu` (9 queries, 21 commands, helpers `MenuCategories`, `MenuItems`, `MenuModifiers`,
  `MenuStations`, `MenuTaxes`, `MenuMapping`).
- `MenuChanges.SaveAsync` replaces its begin/commit pair: save, bump the menu version inside the command's
  transaction (the row lock still serialises concurrent menu writes), queue `MenuChanged` for after the commit.
- Picture upload: the replaced file is deleted after the commit (was: right after the save); the new file is
  still deleted at once when the save fails. Picture removal deletes the file after the commit.
- The "no file" check of the upload endpoint moved from the controller into the command (same error).
- Tax and station writes are gated by `[Authorize(Roles = Admin)]`, not by a permission, so their commands carry
  no `IRequirePermission`; the role gate stays the only check, as before.
- `MenuServiceTests` → `Menu/MenuTests` (signed in as admin). Order tests that change a price or mark an item sold
  out while a waiter is signed in now do that step as the admin (`SendAsAdminAsync`): a waiter has never had
  `menu.manage` / `menu.availability` over HTTP.
