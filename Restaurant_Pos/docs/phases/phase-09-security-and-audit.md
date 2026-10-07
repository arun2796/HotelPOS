# Phase 9 — Security, Permissions & Audit

Status: Not started · Depends on: Phases 1–6

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

- [ ] All endpoints require authentication except login, refresh, health, system info.
- [ ] Waiter cannot reach admin/settings/users; Kitchen cannot reach billing; Cashier cannot write
      menu (verified by the matrix test).
- [ ] Passwords hashed (PBKDF2, ≥ 100k iterations); no plaintext anywhere; tokens not logged.
- [ ] JWT key ≥ 256 bits, generated per installation, stored only on the server.
- [ ] SQL Server not reachable from client PCs (firewall test).
- [ ] Input validation on every request; file uploads type/size checked.
- [ ] Error responses never leak stack traces outside Development.
- [ ] Audit log cannot be modified through the API.

## 12. Acceptance criteria

- [ ] Permission matrix editable and enforced; lockout, rate limiting, device control working.
- [ ] Audit viewer shows a complete trail for the end-to-end scenario.
- [ ] Security checklist completed and recorded in this document.
- [ ] Definition of Done satisfied.

## 13. Risks and notes

- Keep role-based `[Authorize(Roles=...)]` working during migration to policies; convert endpoint by
  endpoint with the matrix test as safety net.
- Rate limiting per IP behind a NAT router counts the whole venue as one IP; keep per-username limits
  primary.

## 14. Changes during implementation

(fill in while building)
