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
      menu (hand-written rows in `AuthorizationTests`, `BillingApiTests`, `PrintApiTests`, `ReportsApiTests`;
      the generated every-endpoint × every-role matrix is still open, see § 15).
- [x] Passwords hashed with ASP.NET Core Identity PBKDF2 (100k iterations); never stored or logged in plain
      text; approval passwords never logged; refresh tokens stored hashed.
- [x] JWT key ≥ 256 bits from `Jwt:SigningKey` (installer-generated in Phase 10; dev key only in
      `appsettings.Development.json`).
- [ ] PostgreSQL not reachable from client PCs — needs the firewall test on the venue network (Phase 10 install checklist).
- [x] Input validation on every request (FluentValidation filter); image uploads checked for type and size (Phase 3).
- [x] Error responses never leak stack traces outside Development (`ExceptionHandlingMiddleware`).
- [x] Audit log cannot be modified through the API (read and CSV export only; the retention job is server-side).

## 12. Acceptance criteria

- [ ] Permission matrix editable and enforced — editable yes; `billing.discount/void/refund`,
      `orders.edit.any/cancel.any`, `tables.view`, `menu.view`, `backup.manage` are not yet enforced anywhere.
- [x] Lockout and rate limiting working (fixes of 2026-10-14 in § 14).
- [ ] Device control — REST 403 and new hub connects refused; open hub connections are not yet dropped.
- [ ] Audit viewer shows a complete trail for the end-to-end scenario — the completeness test checks only
      six actions and not "exactly once".
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

## 15. Remaining work (open)

Server
- [ ] Enforce the permissions that are defined but checked nowhere: `billing.discount`, `billing.void`,
      `billing.refund` (approval is still decided by role), `orders.edit.any`, `orders.cancel.any`,
      `tables.view`, `menu.view`, `backup.manage` (Phase 10). Protected core set for system roles.
- [ ] Deactivating a device must abort its open hub connections (after `ServerNotice`), not only refuse new ones.
- [ ] `DeviceStatusChanged` on hub connect/disconnect (today only on update/activate/deactivate), audience per
      `docs/04` (admins).
- [ ] A disabled terminal can log in under a different device name (device identity is self-declared). Decide:
      bind devices to a registration secret, or document as accepted v1 risk.
- [ ] Inline manager credentials (`ManagerApprovalService`) do not count failures towards lockout yet.
- [ ] Password complexity applied on create/reset too (today only on change-password) and made configurable.
- [ ] Validators for `UpdateDeviceRequest` and `UpdateRolePermissionsRequest`.
- [ ] Audit retention job: first run at start-up, and record the deletion itself in the audit log.

Desktop
- [ ] Manager approval dialog requests and sends approval tokens instead of credentials.
- [ ] Handle `ServerNotice` / 403 `DEVICE_DISABLED` during a session (sign out with a message).
- [ ] Devices screen: assign station; apply renames and last-seen from `DeviceStatusChanged` correctly.
- [ ] Audit Log: user and device filters, custom date range in local time, old/new values side by side.
- [ ] Users: sessions as a list (active only, expired shown as expired), password-policy hint.
- [ ] Navigation driven by permissions, not roles.

Tests
- [ ] Authorization matrix generated from a table for every endpoint × every role (incl. Phase 9 endpoints).
- [ ] Audit completeness test: correct `From` filter, all pages, every § 8 action, "exactly once".
- [ ] Deactivating a device closes an already-open hub connection.
- [ ] Static checks: raw-SQL concatenation grep, secret scan, `dotnet list package --vulnerable`.

Manual
- [ ] Demo script (§ 11 of the template) on two machines, including a network pull during lockout/approval.
