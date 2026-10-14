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

- [x] No `*Service` class left for business use cases; every endpoint dispatches a request (server; see 12.10).
- [x] Pipeline behaviours tested; matrix and audit completeness tests unchanged and green.
- [ ] Definition of Done satisfied.

## 13. Risks and notes

- Large mechanical change: one module per commit, full suite between modules.

## 14. Changes during implementation

The entries 12.1 onwards cover the server (branch `phase-12-cqrs-application-layer`). The desktop `ViewModelBase` /
`ExecuteAsync` part (§ 3, § 4 Desktop row) was not done on this branch, which leaves the desktop projects untouched
while Phase 11 reworks them.

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

**12.4 Orders (2026-10-14)**

- `OrderService` dissolved into `Features/Orders`: queries `GetOrder`, `ListOrders`, `ListActiveOrders` (no
  permission: their endpoints are open to every signed-in role); commands `CreateOrder`, `ReplaceOrderItems`,
  `SubmitOrder`, `AppendOrderItems`, `UpdateOrder`, `CancelOrder`, `ServeOrder`, `CancelOrderItem`
  (`orders.create`, as their endpoints). Helpers `OrderSteps` (loading, ownership, save with concurrency and
  domain-rule mapping, detail and summary projections) and `OrderEvents` (OrderCreated / Updated / Cancelled);
  `OrderItemFactory` moved beside them.
- Create order lost its explicit transaction; tickets and the `Order.Submitted` entry still need the generated
  number, so it still saves twice inside the command's transaction. Table, order and kitchen events are queued in
  the order the service published them and go out after the commit.
- `KitchenSync`, `TicketFactory` and `TableEvents` stay shared and publish immediately; the order handlers queue
  their calls. `request-bill` on the orders controller still calls the billing service until Billing moves.
- `OrderServiceTests` → `Orders/OrderTests`; no other test change was needed.

**12.5 Kitchen (2026-10-14)**

- `KitchenService` dissolved into `Features/Kitchen`: queries `ListKitchenTickets`, `ListCompletedKitchenTickets`
  (`kitchen.operate`), `GetKitchenTicket` (role-gated endpoint, no permission); commands `AcceptKitchenTicket`,
  `StartKitchenTicket`, `MarkKitchenTicketReady`, `RecallKitchenTicket` (`kitchen.operate`) and
  `CompleteKitchenTicket` (role-gated, no permission). The shared state-machine step is `KitchenTickets.ChangeAsync`;
  `KitchenSync` / `TicketFactory` moved to `Features/Kitchen` (billing still uses them).
- `KitchenServiceTests` → `Kitchen/KitchenTests`. Three tests read the kitchen display while a waiter is signed in;
  that read now runs as the admin (a waiter has never had `kitchen.operate` over HTTP).

**12.6 Billing (2026-10-14)**

- `BillingService`, `DiscountService`, `PaymentMethodService` and `ManagerApprovalService` dissolved into
  `Features/Billing`: queries `ListPendingBills`, `ListClosedBills`, `GetBill`, `ListDiscounts`, `ListPaymentMethods`;
  commands `RequestBill`, `ClaimBill`, `ReleaseBill`, `ApplyBillDiscount`, `ClearBillDiscount`, `SetBillCustomer`,
  `FinalizeBill`, `AddPayment`, `CloseBill`, `ReopenBill`, `VoidBill`, `RefundPayment`,
  `ReleaseExpiredBillClaims` (sent by `BillClaimCleanupService`, no user), and the discount / payment-method
  create, update and deactivate commands. Helpers: `BillSteps` (loading, detail and summary projections, the waiter
  visibility rule), `BillWorkflow` (the old `RunAsync` frame plus finalize and settle), `BillEvents`,
  `ManagerApprovals`, `BillTaxBreakup` (also used by the printed invoice).
- `RunAsync` lost its explicit transaction; the old "approvals run before the transaction" rule is kept by
  `ManagerApprovals`: a rejected approval (`Approval.Rejected`) and a wrong approver password (failed-login count,
  `Auth.LoginFailed` / `User.LockedOut`) are written in a scope of their own and committed at once, because the bill
  command that asked for the approval fails and rolls back. The approver is read untracked so a lockout recorded that
  way is seen. New test `Void_WithAWrongApproverPassword_...` checks this through the pipeline.
- `ClearBillDiscount` takes the row version from the query string as a plain value, not as a DTO: the endpoint
  never validated it (a missing row version is a 409 conflict), and wrapping it in `BillActionRequest` would have
  turned that into a 400.
- `request-bill` (orders controller) now sends `RequestBillCommand`. Role-gated endpoints (bill detail,
  request-bill, payment-method writes) carry no `IRequirePermission`; discount writes keep the controller's
  `billing.operate` policy plus their manager role gate.
- `BillingServiceTests` → `Billing/BillingTests`; `AuthServiceTests` resolves `ManagerApprovals` instead of the old
  interface. No assertion changed.

**12.7 Print (2026-10-14)**

- `PrintDocumentService` dissolved into `Features/Print`: queries `GetKitchenTicketDocument`, `GetInvoiceDocument`,
  `GetReceiptDocument` and command `RecordReprint` (the only non-generic `ICommand` so far; it only writes the
  `Print.Reprint` audit entry). All print endpoints are role-gated, so no request carries a permission. Shared
  reads (settings, user names) are in `PrintSources`.
- `PrintDocumentServiceTests` → `Print/PrintTests`; dead `Call<>` helpers left over in the billing, kitchen and order
  tests were removed.

**12.8 Reports (2026-10-14)**

- `ReportService` dissolved into `Features/Reports`: `GetDashboard`, `GetMyDay`, `GetMonthlySalesReport` and nine
  date-range report queries (`GetDailySalesReport`, `GetItemSalesReport`, ... `GetKitchenReport`), all
  `reports.view` except `GetMyDay` (open to every signed-in user, as its endpoint). The range check is
  `ReportRanges`; `IReportQueries` (SQL in Infrastructure), `BusinessDayCalculator` and `ReportCsv` moved beside them.
- `GetMyDay` takes the caller from `ICurrentUser` instead of the controller passing the user id. The CSV / JSON choice
  and file names stay in the controller (HTTP delivery).
- `ReportServiceTests` → `Reports/ReportTests`: report reads run as the admin (`SendAsAdminAsync`; the scenarios sign
  in waiters and cashiers, who never had `reports.view`), and "my day" is read signed in as each waiter.

**12.9 Security, users and auth (2026-10-14)**

- `AuthService`, `UserService`, `PermissionService`, `SessionService` and `ApprovalTokenService` dissolved:
  `Features/Auth` (`Login`, `RefreshToken`, `Logout`, `ChangePassword`, `GetCurrentUser`), `Features/Users`
  (`ListUsers`, `GetUser`, `GetUserSessions`, `CreateUser`, `UpdateUser`, `SetUserActive`, `ResetUserPassword`,
  `UnlockUser`, `LogoutUserEverywhere`; all `users.manage`), `Features/Security` (`ListRoles`, `ListRolePermissions`,
  `GetRolePermissions`, `UpdateRolePermissions`; `roles.manage`; and the anonymous `IssueApprovalToken`). Helpers:
  `AuthTokens`, `UserAdministration`, `RolePermissions` (permissions of a set of roles), `ApprovalTokens` (the
  in-memory single-use token store), `ApprovalRules` and `LoginFailures` (now writing to `IAuditTrail`).
- `Login`, `RefreshToken` and `IssueApprovalToken` are `ICommitOnFailure`: a wrong password must be counted and
  audited, a replayed refresh token revokes every session, a disabled user or device revokes the token, all while
  the answer is a failure. Every other path that fails changes nothing, so committing it is a no-op.
- The billing approval rejection (12.6) is now `RecordApprovalRejectionCommand`, an internal command that
  `ManagerApprovals` sends in a scope of its own, so it runs with its own transaction and audit like any command.
- `SetUserActive` updates the access cache and closes the user's hub connections after the commit (as the service
  did after its save). `CreateUser` lost its explicit transaction.
- `IDeviceService.UpsertAsync` is still used by login until the device module moves (12.10).
- Tests: `AuthServiceTests` → `Auth/AuthTests`, `UserServiceTests` → `Users/UserTests`. `DatabaseTestBase.CreateUserAsync`
  now seeds users directly (as `DbSeeder` does) because the scenarios use demo passwords such as `secret1` that the
  create-user request has always rejected; the create-user tests send valid passwords (`secret123`) and a valid
  username (`w09` instead of `w9`), sign in as admin where they ran without a user, and the "non-admin with
  users.manage" test grants that permission explicitly, as its name says.

**12.10 Operations: devices, settings, server, audit log, backups (2026-10-14)**

- `DeviceService`, `DeviceAdminService`, `SettingsService` (with `ISystemInfoService`), `ServerNoticeService` and
  `AuditQueryService` dissolved: `Features/Devices` (`RegisterDevice`, `UpdateDevice`, `ActivateDevice`,
  `DeactivateDevice`, `TouchDevice`, `ListDevices`, `GetDevice`; helper `DeviceRegistry`, now also used by login) and
  `Features/Operations` (`GetSystemInfo`, `GetPublicSettings`, `GetAllSettings`, `UpdateSettings`,
  `SendServerNotice`, `GetLogTail`, `ListAuditLogs`, `ExportAuditLogs`, `GetBackupOverview`, `RunBackup`).
- Device activation / deactivation update the access cache, send the "terminal disabled" notice, close the
  device's connections and announce its status after the commit, in the order the service did. The hub records
  presence with `TouchDeviceCommand` and reads devices directly; it no longer needs a service.
- The log tail is read through a new port, `ILogTailSource`, implemented by the Api's `LogTailReader`.
- **Deviation: `RunBackupCommand` is a plain MediatR request, not an `ICommand`.** It still goes through logging and
  authorization, but `TransactionBehavior` must not wrap it: `IBackupService` commits the Running row before
  `pg_dump` starts (the row is part of the dump and a server stop leaves it to be closed as interrupted), records a
  failed backup as well as a successful one, and a transaction must not stay open for the minutes a dump can take.
  `IBackupService` stays an Infrastructure port (the daily `BackupHostedService` calls it too).
- `ValidationFilter` (Api) removed: every bound DTO with a validator is now an `IHasInput` request, so the pipeline
  is the single place where FluentValidation runs; the 400 answers are unchanged (Api tests).
- Infrastructure ports that stay as interfaces (they are adapters, not use-case services): `IBackupService`,
  `IAuditService`, `ITokenService`, `IInvoiceNumberService`, `IReportQueries`, `IIdempotencyStore`.
- Docs: `01-architecture.md` (folder layout, request pipeline, § 4 decision, § 6.2 / 6.5 / 6.6, ADR 2) and the
  naming table in `06-conventions-and-dod.md` describe the commands and queries.
- Tests: `SettingsServiceTests` → `Settings/SettingsTests` (signed in as admin). The Api test that makes
  `/api/system/info` throw now substitutes the `GetSystemInfoQuery` handler instead of the removed service; its
  assertions are unchanged.

**Behaviour notes for the whole server migration**

- Steps queued on `IAfterCommit` that throw are logged and do not fail the request (docs/04 § 4 rule 2, now for
  every step). Before, a failing hub disconnect after a user or device deactivation would have answered 500 although
  the change was saved.
- A command that fails after saving now rolls back what it saved. No migrated handler returned a failure after a
  save except the deliberate cases kept by `ICommitOnFailure` (login, refresh, approval token) and the inline
  approval rejection (own command, own transaction).
- Tests that called a service with input the DTO validator rejects, or as a role that never held the endpoint's
  permission, were adjusted in their setup only (listed per module above); no assertion was weakened.
