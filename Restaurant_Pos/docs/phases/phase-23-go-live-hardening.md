# Phase 23 — Go-live hardening

Fixes for the "must fix before go-live" findings of the production-readiness review of 2026-10-22 (security,
reliability and operations reviews of `main` at d1313a2). No new features; every change closes a concrete way to
lose money, duplicate an order, or run a venue on a known secret.

| # | Finding | Fix | Where |
|---|---------|-----|-------|
| 1 | A client that timed out while the order's `COMMIT` was in flight got an exception; the idempotency key was dropped and the retry created a second order and KOT. | Commands commit with `CancellationToken.None`. Orders store the creating `Idempotency-Key` (unique) and the last appended batch's key; a retry that finds its key answers with the existing order instead of a new one. Migration `Phase23_OrderRequestKeys`. | `TransactionBehavior`, `Order.IdempotencyKey/LastAppendKey`, `CreateOrder`, `AppendOrderItems`, `OrdersController` |
| 2 | The cashier screen renewed the payment key on 409 `REQUEST_IN_PROGRESS`, so a third CONFIRM could take a partial payment twice. | `REQUEST_IN_PROGRESS` is handled like a lost connection: key and request are kept, the message says to press CONFIRM again. | `BillDetailViewModel` |
| 3 | Database wait and migrations ran before the host answered the Service Control Manager (30 s limit → error 1053); a failing migration was not logged; no safety copy before migrating. | Start-up runs in `StartupHostedService` (first hosted service, after the SCM is answered, before Kestrel). A verified `pg_dump` (`hotelpos-<stamp>-before-<migration>.dump`) is written before pending migrations on a database that has data; failure to write it stops the start (`Database:BackupBeforeMigrate`). Migration failures are logged with the reason. | `Program.cs`, `StartupHostedService`, `DatabaseInitializer`, `BackupService.DumpBeforeUpgradeAsync` |
| 4 | The production template's `Jwt:SigningKey` placeholder (46 characters) passed the ≥ 32 rule. | Placeholders (`<…>`) and the committed development key are refused at start-up. | `JwtOptions.SigningKeyProblem`, `ApiServiceRegistration` |
| 5 | Empty `Seed:AdminPassword` fell back to `Admin@123`; "must change password" was only a flag the clients honoured. | No fallback: the server refuses to create the administrator without a password; the installer and `configure-server.ps1` require it on a new installation. The API answers 403 `PASSWORD_CHANGE_REQUIRED` outside `/api/auth/*` (and `/api/system/info`) until the password is changed; the browser/Android client gained a change-password page. | `DbSeeder`, `AccessEnforcementMiddleware`, `UserAccessCache`, `server.iss`, `configure-server.ps1`, `Pages/ChangePassword.razor` |
| 6 | `latest.json` and the installer were served anonymously over HTTP and only hash-checked against each other; a spoofed server could push an installer that runs elevated. | The manifest carries an ECDSA signature by the vendor key (`publish.ps1` → `LicenseGenerator sign-update`). The server advertises, and the terminal downloads, only a verified manifest; the SHA-256 in it then ties the installer to it. | `UpdateManifestSignature`, `ClientUpdateSource`, `UpdateService`, `publish.ps1` |
| 7 | `publish-android.ps1` read `<Version>` from `Directory.Build.props`, which only imports `build/version.props`, and always failed. | Reads `build/version.props`. | `build/publish-android.ps1` |
| 8 | .NET 8 leaves support on 2026-11-10. | Not in this phase: a framework move for nine projects, the MAUI workload and the installer runtime gate. Planned as its own task. | — |

## Verification

Full suite green after the changes (see § Log). New tests: duplicate create/append with the same key
(`OrderTests`), payment key kept on `REQUEST_IN_PROGRESS` (`BillingViewModelTests`), start-up without an admin
password refuses (`OperationsApiTests`), `PASSWORD_CHANGE_REQUIRED` round trip (`SecurityApiTests`), signing key rule
(`JwtOptionsTests`), unsigned/tampered/foreign-key manifests refused by server and terminal (`LicensingApiTests`,
`UpdateServiceTests`), `sign-update` CLI (`LicenseTests`), the web change-password page (`ChangePasswordPageTests`).

Not verifiable on the build PC and still open (docs/ops/10 clean-VM test): the service start under the real Service
Control Manager after a cold boot, and the pre-upgrade dump on a venue-sized database.

## Upgrade notes for venues on 0.1.0

- New migration `Phase23_OrderRequestKeys` (two nullable columns and an index on `Orders`); the server writes its own
  safety copy before applying it. Make sure `pg_dump` is found (`Backup:PgBinPath`) or the service will not start.
- Re-run `configure-server.ps1`/the installer as usual; existing settings are kept. Nothing to do for the admin
  account unless it still has the installer's password, in which case the first sign-in now insists on changing it.
- Terminals update only from a signed `latest.json`: publish 0.1.1 with `build/publish.ps1` (vendor key present).

## Log

- 2026-10-22: review (three read-only agents + spot checks) → eight must-fix items; items 1–7 implemented the same day.
