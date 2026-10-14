# Phase 9 — Security, Permissions & Audit

Status: In progress (started 2026-10-08; review and fixes 2026-10-14) · Depends on: Phases 1–6

## 1. Goal

Harden the system for daily operation with staff turnover: fine-grained permissions editable per
role, account lockout and password policy, login rate limiting, device management with online
status, a searchable audit log viewer, standardised manager approvals, and a security review of the
whole API surface. Audit *writing* already exists from Phase 1 onward; this phase makes it visible and
complete.

## 2. Prerequisites

- Phases 1–6. Role and permission constants in `HotelPOS.Contracts.Security`.

## 3. Scope

**In**
- `RolePermissions` table; permissions in JWT claims; API authorization policies per permission
  (replacing role-only attributes where finer control is useful); admin UI to edit role permissions
  (system roles keep a protected core set).
- Password policy (min 8, complexity configurable), forced change on first login, lockout after N
  failures for M minutes, unlock by admin.
- Rate limiting on `/api/auth/login` (per IP and per username).
- Refresh-token rotation audit, "log out everywhere", session list per user.
- Device management: list with online status, rename, assign station, deactivate (connections
  dropped, further requests 403 `DEVICE_DISABLED`), `DeviceStatusChanged` event.
- Audit log viewer with filters (user, device, action, entity, date), detail view with old/new JSON
  diff, CSV export; audit completeness check against the list in § 8.
- Manager approval: short-lived approval tokens (`POST /api/auth/approval` with manager credentials
  returns a 2-minute token bound to action type) as an alternative to sending credentials with every
  approval-requiring request.
- Security review checklist executed (§ 10 and § 11).

**Out**
- HTTPS certificate automation (documented in Phase 10), SSO/AD integration, biometric login.

## 4. Deliverables by project

| Project | Deliverables |
|---|---|
| Contracts | `Permissions` constants (e.g. `orders.create`, `orders.cancel.any`, `billing.discount`, `billing.void`, `menu.write`, `users.manage`, `reports.view`, `settings.manage`), `RolePermissionsDto`, `UpdateRolePermissionsRequest`, `DeviceDto`, `UpdateDeviceRequest`, `AuditLogDto`, `AuditLogQuery`, `UserSessionDto`, `ApprovalTokenRequest/Response`, event `DeviceStatusChanged` |
| Domain | `RolePermission`, `User` lockout fields/methods, `PasswordPolicy` |
| Application | `IPermissionService`, `AuthService` lockout + policy + approval tokens, `IDeviceAdminService`, `IAuditQueryService`, `ISessionService` |
| Infrastructure | `Phase09_Security` migration (`RolePermissions`, user lockout columns, device columns), seed default permission sets per role |
| Api | permission policies (`[Authorize(Policy = Permissions.X)]`) + `PermissionHandler`, rate limiter, `AdminDevicesController`, `AuditLogsController`, `RolesController.Permissions`, hub drops deactivated devices |
| Desktop | Admin: Roles & Permissions matrix, Devices (online badge, rename, station, deactivate), Audit Log (filters, detail diff, export), user sessions; Manager approval dialog uses approval tokens; lockout/expiry messages on login |
| Tests | see § 10 |

## 5. Data model

`RolePermissions(RoleId, Permission)`; `Users` adds `FailedLoginCount`, `LockedUntil`,
`PasswordChangedAt`; `Devices` adds `StationId`, `DeactivatedAt`. Migration `Phase09_Security`.

## 6. API endpoints

`docs/03-api-reference.md` § 3.2 (unlock, role permissions) and § 3.10 (audit logs, devices), plus
`POST /api/auth/approval`, `GET /api/users/{id}/sessions`, `POST /api/users/{id}/logout-all`.

## 7. Real-time events

`DeviceStatusChanged` to admins on connect/disconnect/deactivate; `ServerNotice` to a deactivated
device before dropping its connection.

## 8. Business rules

- Permission sets: Admin = all (not editable); Manager default = everything except users/roles/
  settings/devices/backup; Waiter, Kitchen, Cashier defaults as per module tables in `docs/00` § 3.
- A permission removed from a role takes effect at next token refresh (≤ 60 min) or immediately when
  the admin chooses "force re-login for this role" (revokes refresh tokens).
- Lockout: 5 failures -> 15 minutes (settings `LockoutThreshold`, `LockoutMinutes`); admin unlock.
- Rate limit: 10 login attempts per minute per IP, 5 per username; 429 with `Retry-After`.
- Approval token: issued only to Manager/Admin credentials, bound to an action type and valid 2
  minutes, single use.
- Deactivated device: hub connection closed, every request 403; re-activation needed.
- Audit completeness (must exist by the end of this phase): user/role/permission changes, login
  failures and lockouts, device changes, settings changes, menu price/availability changes, order
  submit/append/cancel/item cancel/serve, ticket transitions, bill request/claim override/discount/
  finalize/payment/refund/void/reopen, reprints, backups (Phase 10).
- Audit logs are append-only; no API deletes them. Retention policy configurable (default keep all).

## 9. Desktop screens

| Screen | Behaviour |
|---|---|
| Roles & Permissions | Matrix of roles x permission groups with checkboxes; save with confirmation; "force re-login" option |
| Devices | Grid: name, type, station, machine, app version, last seen, online badge; rename, assign station, deactivate/activate |
| Audit Log | Filters, paged grid, detail panel with old/new values side by side, export CSV |
| Users (extended) | Lockout state and unlock, sessions list, logout everywhere, password policy hints |
| Login | Messages for locked account, disabled device, expired session |

## 10. Tests

- Authorization matrix test: for every endpoint, every role -> expected 200/403 (generated from a
  table; this is the regression net for permission edits).
- Lockout after 5 failures, unlock by admin, automatic unlock after the window.
- Rate limiter returns 429 and `Retry-After`.
- Refresh token reuse detection revokes the chain.
- Approval token: wrong role rejected, expired rejected, single use.
- Deactivated device: hub disconnected, REST 403.
- Audit completeness test: run the end-to-end scenario (order -> kitchen -> bill -> payment ->
  refund) and assert each expected audit action exists exactly once.
- Static checks: no raw SQL string concatenation (grep in CI), no secrets in repo, dependency
  vulnerability scan (`dotnet list package --vulnerable`).

## 11. Manual security review checklist

- [x] All endpoints require authentication except login, refresh, approval, health, system info
      (fallback policy `RequireAuthenticatedUser`; `AuthorizationTests` covers unknown routes).
- [x] Waiter cannot reach admin/settings/users; Kitchen cannot reach billing; Cashier cannot write
      menu (`AuthorizationMatrixTests`: every endpoint × every role against `authorization-matrix.txt`).
- [x] Passwords hashed with ASP.NET Core Identity PBKDF2 (100k iterations); never stored or logged in plain
      text; approval passwords never logged; refresh tokens stored hashed.
- [x] JWT key ≥ 256 bits from `Jwt:SigningKey` (installer-generated in Phase 10; dev key only in
      `appsettings.Development.json`).
- [ ] PostgreSQL not reachable from client PCs — needs the firewall test on the venue network (Phase 10 install checklist).
- [x] Input validation on every request (FluentValidation filter); image uploads checked for type and size (Phase 3).
- [x] Error responses never leak stack traces outside Development (`ExceptionHandlingMiddleware`).
- [x] Audit log cannot be modified through the API (read and CSV export only; the retention job is server-side).

## 12. Acceptance criteria

- [x] Permission matrix editable and enforced (every defined permission is checked; `backup.manage` waits for
      the Phase 10 backup endpoints). Guarded by `AuthorizationMatrixTests` + `authorization-matrix.txt`.
- [x] Lockout and rate limiting working (fixes of 2026-10-14 in § 14).
- [x] Device control — REST 403, new hub connects refused, open hub connections closed after `ServerNotice`.
- [x] Audit viewer shows a complete trail for the end-to-end scenario (`AuditCompleteness_EndToEndScenario_*`:
      exact trail, each action once).
- [x] Security checklist completed and recorded in this document (one item deferred to the install checklist).
- [ ] Definition of Done satisfied (open items in § 15).

## 13. Risks and notes

- Keep role-based `[Authorize(Roles=...)]` working during migration to policies; convert endpoint by
  endpoint with the matrix test as safety net.
- Rate limiting per IP behind a NAT router counts the whole venue as one IP; keep per-username limits
  primary.

## 14. Changes during implementation

- **Permissions in the token.** Role permissions are loaded from `RolePermissions` at login and refresh and
  carried as claims; `PermissionAuthorizationHandler` checks the claim (Admin always passes). Endpoints now
  use `[Authorize(Policy = Permissions.X)]` for orders, kitchen, menu, floor, billing, discounts, users,
  roles, settings, reports, devices and audit logs. Endpoints whose audience is not one permission (waiter
  read of a bill, kitchen runners, print documents, request-bill, payment-method/tax/station writes) keep
  role attributes. Defaults seeded per role equal the Phase 1 map, so the matrix tests did not change.
- A class-level role attribute combined with a method-level one must *both* pass in ASP.NET Core; the
  billing controller had that combination and was silently refusing waiters on `GET /api/billing/{id}`.
  The class now carries `[Authorize]` only and each desk action its own policy.
- **Approval tokens** live in server memory (lost on restart, which only cancels tokens younger than two
  minutes). `ManagerApprovalService` takes an explicit action type (`ApprovalActions`) rather than guessing
  it from the message; the bill endpoints pass `discount`, `void`, `refund` or `reopen`.
- **Rate limiter** uses the socket address only; `X-Forwarded-For` is ignored because a client could choose
  its own bucket with it. Per-username limiting remains the primary control (one venue = one NAT address).
- **Device enforcement** caches the deactivated flag per device in memory (`IDeviceAccessCache`) and drops
  it on activate/deactivate, so the per-request check does not query the database every time.
- **Audit retention**: `AuditRetentionDays` (0 = keep all) drives a six-hourly server job; nothing else
  deletes audit rows.
- New settings: `LockoutThreshold` (5), `LockoutMinutes` (15), `AuditRetentionDays` (0).

**Review of 2026-10-14.** The work above was written on 2026-10-08 but never committed; it was brought onto
branch `phase-09-security-and-audit`, reviewed against this document, and these defects were fixed:

- **Permission edits did not survive a restart.** `DbSeeder` re-added every default permission missing from a
  role on each start, and `PermissionService` fell back to the default map for a role with no rows. Defaults are
  now seeded only for roles the seeder creates; the `Phase09_Security` migration back-fills databases whose roles
  existed before Phase 9; the database is the only source afterwards (an emptied role stays empty).
- **Lockout re-locked after it expired.** `FailedLoginCount` survived the lock, so the first typo after 15
  minutes locked the account again. `User.RecordFailedLogin(now, threshold, duration)` now starts a fresh count
  after an expired lock; attempts against a locked account are audited (`Auth.LoginFailed`, reason `Locked`).
- **Approval endpoint allowed unlimited password guessing.** `POST /api/auth/approval` is anonymous, was not
  rate-limited and did not count failures. Wrong passwords now count towards the same lockout as login
  (`LoginFailures`), and the endpoint is rate-limited by `approverUsername`.
- **Rate limiter**: a trailing slash or other casing (`/api/auth/login/`) skipped it; its dictionaries never
  shrank; check and record were not atomic; a bare `catch` hid every error. All fixed.
- **Approval token** with a null or unknown `actionType` gave a 500 (now 400 from `ApprovalTokenRequestValidator`);
  a token presented for the wrong action was spent anyway (now rejected without being spent).
- **Device cache race**: the cache entry was dropped before the commit, so a request in between could cache
  "active" for good. The new state is now written to the cache after the commit.
- **Audit CSV formula injection**: cells starting with `= + - @` (e.g. the username of a failed login) are
  prefixed with `'`.
- **Privilege escalation**: a non-Admin holding `users.manage` could create an Admin account or reset an
  admin's password. Admin accounts and the Admin role can now be managed by Admins only.

**Second round, 2026-10-14** (items closed from § 15):

- **Disabled devices lose live connections.** `ConnectionTracker` keeps each connection's context; deactivation
  sends `ServerNotice`, waits 500 ms and aborts the device's connections. The hub tracks a connection before
  checking its device, so a deactivation committed during the check still closes it.
- **Presence**: `DeviceStatusChanged` also fires when a device's first connection opens or its last one closes,
  with `isActive` read back from the database (a disconnect caused by deactivation is not announced as active).
- **Permissions are enforced, not just stored.** `ICurrentUser.HasPermission` (Admin always true).
  `orders.cancel.any` and `orders.edit.any` replace the Manager role checks in `OrderService`; `billing.discount`
  guards applying/clearing a discount; `tables.view` and `menu.view` guard the floor and menu reads.
  `ApprovalRules`: a caller holding `billing.void` voids or reopens without approval and only holders may approve
  it; the same for `billing.refund`. An over-limit discount has no permission of its own, so its approval stays with
  the Manager and Admin roles.
- Kitchen's default set gained `tables.view`: the table map has been readable by every role since Phase 2.
- **Matrix test**: `AuthorizationMatrixTests` derives who may call each of the ~130 controller endpoints from the
  `[Authorize]` metadata and the default sets, compares it with the reviewed `authorization-matrix.txt`, calls every
  denied cell over HTTP (must be 403) and checks every allowed cell with the real authorization service.
- Inline manager credentials count towards lockout like login and approval tokens.
- Password complexity (letters and digits) on create, reset and change; validators for `UpdateDeviceRequest`
  and `UpdateRolePermissionsRequest` (unknown permissions are rejected, not silently dropped).
- Audit retention runs one minute after start, then every six hours, and records `Audit.Purged`.

**Third round, 2026-10-14** (desktop and tests):

- **Approval tokens on the desktop.** `Approvals.WithApprovalAsync` exchanges the manager's credentials once at
  `POST /api/auth/approval` (bound to the action type) and sends only the token with the void/refund/reopen/discount
  call, so a retried or logged business request never carries the password. Locked and rate-limited answers get
  their own message.
- **Disabled terminal mid-session.** The navigator signs out on `ServerNotice` with `DEVICE_DISABLED`, and
  `AuthDelegatingHandler` does the same on any 403 `DEVICE_DISABLED` (body buffered so callers still read it).
- **Devices screen**: assign a kitchen station (`DeviceDto.StationName` shown); renames and presence applied from
  `DeviceStatusChanged`, last-seen moved only on an online/offline edge. Fixed: success was judged by
  `Data != null`, but update/activate/deactivate answer without data, so every success showed an error toast.
  Unknown station ids are a validation error (was a 500 from the foreign key).
- **Audit Log**: user and terminal filters (server gained `deviceName`), custom date range, local days for every
  preset (was the UTC date), before/after side by side, and the CSV export sends the same filters as the grid (it
  dropped user, terminal and entity id). `AuditLogQuery` is validated (page size at most 200).
- **Users**: the sessions list shows unrevoked, unexpired tokens only (each refresh rotates the token, so revoked
  rows were mostly rotations) in local time; password hint; complexity checked on reset and change.
- **Navigation follows permissions** (`ModuleRegistry.ForUser`); with the default sets every role sees the same
  modules as before (tested per role). `RolePermissionMap` moved to `HotelPOS.Contracts.Security` so the desktop
  and its tests share the default sets.
- **Audit completeness**: the end-to-end scenario (order, kitchen, bill, discount, finalize, payment, reprint,
  refund with an approval token) must leave exactly its expected trail on its order, tickets and bill, each action
  once. Kitchen ticket actions became `AuditActions` constants.
- **Static checks** run with `dotnet test` (`StaticSecurityChecksTests`: no `*Raw` SQL APIs, `SqlQuery`/`ExecuteSql`
  only with an interpolated literal). `dotnet list package --vulnerable` found CVE-2026-70159 (SignalR DoS) in
  `Microsoft.AspNetCore.SignalR.Common` 8.0.31; ASP.NET Core packages raised to 8.0.32, scan clean.

**Found during Phase 10 (2026-10-14).** Deactivating a user revoked their refresh tokens, but the access token they
already held kept working for up to an hour, and their hub connections stayed open. `AccessEnforcementMiddleware`
(was `DeviceEnforcementMiddleware`) now refuses a deactivated user on every request with 403 `ACCOUNT_DISABLED`
(cached like devices, set after the admin's commit); the hub refuses their new connections and the deactivation
closes the open ones; the desktop signs out with the reason, as for a disabled terminal.

## 15. Remaining work (open)

Server
- [x] A disabled terminal can log in under a different device name (device identity is self-declared).
      **Accepted v1 risk (decided 2026-10-14):** a valid user account is still required, every action is audited
      with user and device, and admins can disable the user. Device binding (a per-terminal secret) can come with
      the Phase 10 installer.
- [ ] Protected core permission set for the non-Admin system roles (spec § 3); today only Admin is protected.
- [ ] Password complexity configurable (fixed at letters + digits, minimum 8).
- [x] Secrets in the repository: the PostgreSQL password left `appsettings.Development.json` and
      `tests/Shared/TestPostgres.cs` (now user-secrets and `HOTELPOS_TEST_POSTGRES`, see `docs/dev-setup.md`);
      `StaticSecurityChecksTests` fails on any committed connection-string password. Earlier commits still contain
      the old value, so it should be changed if it is used anywhere else.
- [ ] Server PCs must run ASP.NET Core runtime 8.0.32 or later (the server's SignalR comes from the shared
      runtime, not from NuGet); add to the Phase 10 install checklist.

Manual
- [ ] Demo script (§ 11 of the template) on two machines, including a network pull during lockout/approval.
