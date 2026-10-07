# Phase 1 — Foundation

Status: **Done** (2026-10-07) · Depends on: Phase 0 (planning)

## 1. Goal

Produce a runnable skeleton of the whole system: solution and projects, SQL Server database with
migrations and seed data, API with JWT authentication, users administration, SignalR hub, global
error handling and logging, and a WPF shell that can be configured on first run, log in, show
role-based navigation and display live connection status. No restaurant business features yet.

## 2. Prerequisites

- Decisions in `docs/01-architecture.md` § 9 (enums in Contracts, plain services, int keys, UTC).
- Developer machines: .NET 8 SDK, SQL Server LocalDB or SQL Server Express, Visual Studio 2022 /
  Rider / VS Code.

## 3. Scope

**In**
- `HotelPOS.sln`, six `src` projects, three `tests` projects, `Directory.Build.props`,
  `Directory.Packages.props`, `.editorconfig`, `.gitignore`.
- Domain base types, identity entities, system entities (Settings, AuditLog, IdempotencyRecord).
- EF Core `AppDbContext`, entity configurations, auditing interceptor, `Phase01_Initial` migration,
  seeder (roles, admin, default settings, payment methods, MAIN station — station/payment method
  entities are created now to avoid a second seed step later).
- Application: `Result<T>`, error codes, `ICurrentUser`, `IClock`, `IAppDbContext`, `IAuditService`,
  `IRealtimeNotifier` (no-op events yet), `AuthService`, `UserService`, `DeviceService`,
  `SettingsService`, validators.
- Infrastructure: `JwtTokenService`, `PasswordHasher` adapter, `SystemClock`, `AuditService`,
  `SignalRRealtimeNotifier`.
- API: `Program.cs` composition, Serilog, correlation id, exception middleware, response envelope,
  Swagger (Development only), health check, JWT bearer (with hub query-string support), controllers
  (`Auth`, `Users`, `Roles`, `Devices`, `System`, `AdminSettings`), `RestaurantHub` with role groups
  and `ConnectionTracker`.
- Contracts: envelope, paging, auth/user/device/settings DTOs, enums, `Roles` constants,
  `HubEvents` constants, `RealtimeEvent` base record.
- Desktop: host bootstrap, settings service, first-run configuration window, login window, shell with
  role-based sidebar, status bar with connection indicator, `ApiClient` + auth handler, DPAPI session
  store, `IRealtimeClient` with reconnect policy, placeholder pages per module, Users admin screen,
  Server/Device settings screen, Serilog.
- Tests as listed in § 10.
- `docs/dev-setup.md` (how to run locally).

**Out**
- Tables, menu, orders, kitchen, billing, printing, reports (later phases).
- Permission editing, lockout, rate limiting (Phase 9).
- Windows Service hosting and installers (Phase 10).

## 4. Deliverables by project

| Project | Deliverables |
|---|---|
| Contracts | `ApiResponse<T>`, `ApiError`, `PagedResult<T>`, `LoginRequest/Response`, `RefreshTokenRequest`, `ChangePasswordRequest`, `UserDto`, `CreateUserRequest`, `UpdateUserRequest`, `ResetPasswordRequest`, `RoleDto`, `RegisterDeviceRequest/Response`, `SystemInfoDto`, `SettingDto`, enums (`DeviceType`, `SettingDataType`, plus all enums from `docs/02` § 3 so later phases don't churn the project), `Roles`, `HubEvents`, `RealtimeEvent` |
| Domain | `BaseEntity`, `IAuditable`, `DomainException`, `User`, `Role`, `UserRole`, `RefreshToken`, `Device`, `Setting`, `AuditLog`, `IdempotencyRecord`, `PaymentMethod`, `PreparationStation` |
| Application | `Result`, `Result<T>`, `AppError`, `ErrorCodes`, interfaces (`IAppDbContext`, `ICurrentUser`, `IClock`, `IAuditService`, `IRealtimeNotifier`, `ITokenService`, `IPasswordHasher`), `AuthService` (login, refresh, logout, change password), `UserService` (CRUD, activate/deactivate, reset password), `DeviceService` (register, touch last seen), `SettingsService` (public/all/update), FluentValidation validators |
| Infrastructure | `AppDbContext` + configurations, `AuditableEntityInterceptor`, `Phase01_Initial` migration, `DbSeeder`, `JwtTokenService`, `PasswordHasherAdapter`, `SystemClock`, `AuditService`, `SignalRRealtimeNotifier`, DI extension `AddInfrastructure()` |
| Api | `Program.cs`, `appsettings.json` (+ Development), middleware (`CorrelationIdMiddleware`, `ExceptionHandlingMiddleware`), `ApiResponseFilter`, `ValidationFilter`, controllers, `RestaurantHub`, `ConnectionTracker`, `CurrentUser` (claims adapter), health check |
| Desktop | `App`, `HostBuilder`, `ClientSettingsService`, `SecureStore`, `ApiClient`, `AuthDelegatingHandler`, `AuthSession`, `RealtimeClient`, `ConnectionState`, `NavigationService`, `ModuleRegistry`, `DialogService`, `NotificationService`, views/VMs: `FirstRunConfig`, `Login`, `Shell`, `ServerSettings`, `ChangePassword`, `Users` (admin), placeholders for all other modules; themes (colors, typography, buttons, light/dark) |
| Tests | `Domain.Tests` (project + smoke), `Application.Tests` (AuthService, UserService), `Api.Tests` (`ApiFactory`, auth, authorization, envelope, correlation, hub connect) |

## 5. Data model

Entities: `Users`, `Roles`, `UserRoles`, `RefreshTokens`, `Devices`, `Settings`, `AuditLogs`,
`IdempotencyRecords`, `PaymentMethods`, `PreparationStations` (columns in `docs/02` § 2.1).
Migration: `Phase01_Initial`. Seed: `docs/02` § 8.

## 6. API endpoints

See `docs/03-api-reference.md` § 3.1 and § 3.2 (Phase 1 rows): auth (login, refresh, logout, me,
change-password), `/health`, `/api/system/info`, `/api/settings`, `/api/admin/settings`,
`/api/devices/register`, `/api/users/*`, `/api/roles`.

## 7. Real-time events

None published yet. The hub accepts authenticated connections, assigns `role:*`, `user:{id}` and
`device:{id}` groups, tracks presence, and answers `Ping`.

## 8. Business rules

- Username unique, case-insensitive; password min 6 chars (policy tightened in Phase 9).
- Login fails with a generic `INVALID_CREDENTIALS` message (never reveals which part was wrong).
- Access token 60 min (`Jwt:AccessTokenMinutes`), refresh token 12 h, rotated on each refresh; a reused
  revoked refresh token revokes the whole chain.
- Inactive users cannot log in or refresh; deactivating a user revokes their refresh tokens.
- The last active Admin cannot be deactivated or lose the Admin role.
- `MustChangePassword` forces the change-password screen after login.
- Device registration is idempotent on `(name)`; re-registering updates machine name/app version.
- Audit: `User.Created`, `User.Updated`, `User.RoleChanged`, `User.Deactivated`,
  `User.PasswordReset`, `Auth.LoginFailed`, `Settings.Updated`.

## 9. Desktop screens

| Screen | Behaviour |
|---|---|
| First-run configuration | Fields: API URL, device name, device type (station in Phase 5). **Test connection** calls `/api/system/info` and shows restaurant name + version or a clear error. **Save** writes `settings.json`. |
| Login | Username, password, remembered username, "Server settings" link, inline error on failure, spinner while calling. |
| Shell | Sidebar modules by role, top bar (restaurant, user, device), status bar (🟢/🟡/🔴, version). Placeholders for modules not yet built show "Coming in Phase N". |
| Users (Admin) | Grid (search, active filter), side panel add/edit (display name, username, roles, active), reset password, deactivate/activate. |
| Server / Device settings | Same fields as first-run; saving requires re-login. |
| Change password | Current/new/confirm; forced when `MustChangePassword`. |

## 10. Tests

- Application: login success returns tokens; wrong password fails; inactive user fails; refresh
  rotates and old token is rejected; last admin cannot be deactivated; password is hashed (never equal
  to plaintext).
- Api: `/health` 200; login 200/401; `GET /api/users` as Waiter -> 403, as Admin -> 200; unknown route
  returns envelope; thrown exception -> 500 envelope with correlation id; `X-Correlation-Id` echoed;
  hub connection with valid token succeeds and without token fails; `GET /api/system/info` anonymous.
- Desktop: `LoginViewModel` shows error on failure; `FirstRunConfigViewModel` validates URL format;
  `ConnectionState` transitions (Connected -> Reconnecting -> Connected) update the indicator.

## 11. Manual demo script

1. Fresh SQL Server: run the API; confirm migration + seed ran (log lines) and `/health` returns 200.
2. Start the desktop with no `settings.json`: first-run screen appears. Enter a wrong URL -> clear
   error. Enter the right URL -> restaurant name shown. Save.
3. Log in as `admin`: forced password change, then shell with Admin modules.
4. Create users `waiter1` (Waiter), `kitchen1` (Kitchen), `cashier1` (Cashier).
5. On a second machine (or second instance with a different `settings.json` path via
   `--settings` argument), log in as `waiter1`: only waiter modules appear.
6. Stop the API: both clients show 🔴 within 10 s and do not crash. Start it again: 🟢 within 20 s
   with no user action.
7. Check `logs/api-.log` contains the login, with no password or token.

## 12. Acceptance criteria

- [x] `dotnet build` and `dotnet test` succeed from a clean clone (0 warnings; 155 tests).
- [x] Clean database created by migration; seed data present; `admin` can log in.
- [x] JWT auth and role authorization work end-to-end (403 for wrong role).
- [x] Response envelope, correlation id and exception middleware behave as in `docs/03` § 2.
- [x] Desktop first-run config, login, role-based shell, settings screen work.
- [x] Connection indicator reflects hub state and recovers automatically (end-to-end test kills and restarts the API).
- [x] Users admin screen can create a user of each role.
- [x] No IP/URL/secret is compiled into the desktop.
- [x] `docs/dev-setup.md` written; Definition of Done satisfied.

## 13. Risks and notes

- Writing `%ProgramData%\HotelPOS\Desktop\settings.json` needs an ACL granting Users modify rights;
  until the installer exists, fall back to `%LocalAppData%` if ProgramData is not writable.
- Keep the hub group assignment logic in one place (`HubGroups`) so Phase 5 can add station groups.
- Creating all enums now (even unused) avoids churn in Contracts for every phase.

## 14. Changes during implementation

Recorded 2026-10-07. The reference documents were updated where noted.

**Structure and naming**

- Domain namespace `Administration` (Setting, AuditLog, IdempotencyRecord) instead of `System`, which would
  shadow the .NET `System` namespace inside the Domain project.
- API cross-cutting code lives in `HotelPOS.Api/Common` (middleware, filters, envelope writer, current user)
  instead of `Middleware/` and `Filters/`; `Infrastructure` was avoided as a folder name inside the API.
- `SignalRRealtimeNotifier` lives in `HotelPOS.Api/Hubs` (it needs the hub type), not in Infrastructure.
- DTOs of one feature share a file (`AuthContracts.cs`, `UserContracts.cs`...). Classes with behaviour stay one per file.

**Behaviour**

- Login also registers/updates the device (`deviceName`, `deviceType`, `machineName`, `appVersion` in the
  login request) and returns `deviceId`; the token carries `device_id`/`device_name` claims.
  `POST /api/devices/register` still exists for explicit registration.
- `POST /api/auth/logout` is anonymous: possession of the refresh token is the authorization, so a user
  with an expired access token can still sign out cleanly.
- Every endpoint requires authentication unless marked anonymous (authorization fallback policy). As a
  consequence an anonymous request to an unknown route returns 401, an authenticated one 404.
- New error codes `DUPLICATE` (409) and `METHOD_NOT_ALLOWED` (405); client-only code `CONNECTION_UNAVAILABLE`.
- `SettingDataType.Time` added for `BusinessDayStartTime` (HH:mm).
- `Users.NormalizedUsername` (upper-case) carries the unique index, so usernames are case-insensitive
  regardless of the database collation.
- Updating only a user's roles still bumps the user's row version (the user row is always written), so
  concurrent role edits are detected.
- Failed logins increment `FailedLoginCount`; locking the account is Phase 9.
- The client resumes the previous session after a restart (refresh token stored with DPAPI) unless the
  user signed out.

**Desktop**

- `--profile NAME` replaces the `--settings` argument mentioned in the demo script: each profile has its
  own settings, session, preferences and log file.
- One window: configuration, login, password change and the shell are screens inside `MainWindow`;
  dialogs and toasts are overlays in the same window (no OS message boxes).
- Settings are written to ProgramData when possible, otherwise to the user profile; the most recent file wins.
- `GET` requests are retried twice on connection failures; writes are not retried until idempotency keys
  arrive in Phase 4. The server check on the login/configuration screens uses a 5-second timeout and no retry.
- Screens and pages are created with `ActivatorUtilities` (not registered as transients) so the container
  does not keep every disposed view-model alive.

**Tests added beyond the plan**

- `ScreenRenderingTests`: renders all screens with sample data, fails on XAML/binding errors and writes
  screenshots to `%TEMP%\hotelpos-screens`.
- `ConnectionResilienceTests` (opt-in, `HOTELPOS_E2E=1`): real API process killed and restarted under a
  signed-in client.

**Demo script results (2026-10-07)**

- Steps 1, 3, 4, 7 executed against the real API (curl + desktop UI Automation): migration and seed on a
  fresh database, admin forced password change flow, users of each role created, no password or token in
  the API or desktop logs.
- Step 6 (server stop/start) covered by the end-to-end test; the client went amber/red and back to green
  without user action and kept its session.
- Steps 2 and 5 (first-run screen, second terminal) verified with the rendering test and the `--profile`
  option on one PC; a two-PC run over Wi-Fi is still to be done on the target hardware.
