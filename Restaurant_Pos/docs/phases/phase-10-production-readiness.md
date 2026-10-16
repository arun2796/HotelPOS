# Phase 10 — Production Readiness

Status: In progress (started 2026-10-14; Phase 9 open items in its § 15) · Depends on: all previous phases

## 1. Goal

Make the system installable, operable and recoverable by a technician at the venue: the API runs as a
Windows Service with automatic migrations, installers set up server and clients, backups run on a
schedule and can be restored, logs and health are observable, and documentation covers LAN setup,
firewall, upgrade, troubleshooting and daily operations.

## 2. Prerequisites

- All functional phases complete; `docs/01` § 2 network layout.

## 3. Scope

**In**
- API as Windows Service (`UseWindowsService`), Kestrel binding from configuration, optional HTTPS
  with a self-signed or internal certificate, auto-migrate on start, startup self-checks.
- Server installer (Inno Setup): prerequisites check (.NET 8 runtime / self-contained publish,
  PostgreSQL reachable), connection string + admin password wizard, JWT key generation, firewall rule,
  service registration, first migration + seed, desktop shortcut for the admin client.
- Client installer: desktop app, ProgramData settings folder with ACL, shortcut, optional
  pre-filled `ApiBaseUrl` passed as installer parameter (`/API=http://192.168.1.100:5000`).
- Backup: `BackupHostedService` (daily at configured time -> `pg_dump --format=custom` plus a copy of the `Media:RootPath` folder
  with menu pictures, retention
  cleanup, verify), admin **Backup now**, backups list, restore procedure and script.
- Monitoring: `/health` with DB and disk checks, Serilog rolling files with retention, admin
  log tail screen, `ServerNotice` broadcast, version compatibility check (`minClientVersion`).
- Performance validation on venue-like hardware (20 clients, 500-item menu).
- Documentation set in `docs/ops/`: installation, LAN & firewall, backup & recovery, upgrade,
  troubleshooting, daily operations, go-live checklist.

**Out**
- Cloud sync, remote access/VPN, automatic updates over the internet (document manual upgrade).

## 4. Deliverables by project

| Project | Deliverables |
|---|---|
| Api | Windows Service hosting, `appsettings.Production.json` template, startup checks (DB reachable, migrations applied, settings seeded, disk space), HTTPS configuration section, `AdminController` (backup now, backups list, log tail), `ServerNotice` endpoint |
| Infrastructure | `BackupService` (`pg_dump -Fc` to a timestamped file, verify with `pg_restore --list`), `BackupHostedService` (schedule from settings), retention cleanup |
| Contracts | `BackupDto`, `ServerNoticeRequest`, `LogTailDto`, `VersionInfo` |
| Desktop | Version check on startup (blocks with message when below `minClientVersion`), Admin: Backup screen (now/list/settings), Log viewer, Server notice sender; graceful shutdown handling of `ServerNotice` |
| Installers | `installer/server.iss`, `installer/client.iss`, `build/publish.ps1` (self-contained x64), `build/version.props` |
| Scripts | `scripts/backup-now.ps1`, `scripts/restore-db.ps1`, `scripts/create-firewall-rule.ps1`, `scripts/set-static-ip.md` |
| Docs | `docs/ops/01-installation.md`, `02-lan-and-firewall.md`, `03-backup-and-recovery.md`, `04-upgrade.md`, `05-troubleshooting.md`, `06-daily-operations.md`, `07-go-live-checklist.md` |
| Tests | see § 10 |

## 5. Data model

`Settings` keys: `Backup:Path`, `Backup:Time`, `Backup:RetentionDays`, `Backup:Enabled`. Possible
`Backups` table (file, size, started/finished, status, triggered by) or derive from the folder;
prefer the table for the admin list and audit (`Backup.Completed/Failed`). Migration `Phase10_Ops`.

## 6. API endpoints

`docs/03-api-reference.md` § 3.10: backup, backups, logs; plus `POST /api/admin/notice`.

## 7. Real-time events

`ServerNotice { level, message }` (e.g. "Server restarting in 2 minutes for maintenance").

## 8. Business rules / operational rules

- Service account: Local System or a dedicated local account with rights to the backup folder; the
  PostgreSQL role used by the API owns the `hotelpos` database only (no superuser).
- Backups: daily full backup at `Backup:Time` (default 03:30, before business day start), retention
  default 14 days, written to `Backup:Path` (local disk + recommended copy to USB/NAS documented);
  failure raises an admin notice at next login and audit entry.
- Restore: documented step-by-step with the script; always tested at go-live.
- Upgrade: stop service -> backup -> run server installer -> auto-migrate on start -> verify health
  -> update clients (clients below `minClientVersion` are blocked with a clear message).
- Logs: API `C:\ProgramData\HotelPOS\Api\logs`, 30-day retention, max 50 MB per file; desktop
  `%LocalAppData%\HotelPOS\logs`, 14 days.
- Firewall: inbound TCP 5000 (and 5001 for HTTPS) on Private profile only; SQL port closed.
- Static IP for the server (router DHCP reservation preferred); hostname alternative documented
  (`http://hotelpos-server:5000`) when the router resolves names.
- HTTPS (recommended when any non-trusted device can join the Wi-Fi): self-signed certificate
  generated by the installer, exported `.cer` installed on clients by the client installer parameter
  `/CERT=path`.

## 9. Desktop screens

| Screen | Behaviour |
|---|---|
| Admin Backup | Settings (path, time, retention, enabled), **BACKUP NOW**, list with status/size/duration, last failure banner |
| Admin Logs | Tail of API log (last N lines, refresh), filter by level |
| Admin Server Notice | Message + level -> broadcast |
| Startup | Version mismatch screen with required version and where to get it |

## 10. Tests

- `BackupService` integration test against PostgreSQL: produces a file, verify succeeds, retention
  deletes old files, failure audited.
- Startup checks fail fast with clear log messages when DB unreachable (test via wrong connection
  string in a `WebApplicationFactory` variant).
- Health endpoint reports degraded on low disk (threshold mocked).
- Version check: client below minimum is rejected by `/api/system/info` consumer logic (desktop test).
- Installer smoke test (manual, documented): clean Windows VM -> server installer -> client installer
  -> login -> full order-to-payment flow -> restore from backup on a second VM.
- Load test script (`k6` or a small .NET console) : 20 virtual clients ordering/billing for 10
  minutes; API p95 < 300 ms on LAN; no deadlocks; SignalR events delivered.

## 11. Manual demo script (go-live rehearsal)

1. Clean server VM: run server installer with PostgreSQL installed; service starts; `/health` OK;
   firewall rule present; admin password set during install works.
2. Two client VMs: run client installer with `/API=`; first login; device registration; full flow
   (order -> kitchen -> bill -> payment -> print to PDF printer).
3. Trigger **Backup now**; copy the file to a third VM; run restore script; start API there; data
   present.
4. Simulate server reboot during service: clients show 🔴, then 🟢 and resync automatically.
5. Upgrade rehearsal: install a newer build over the existing one; migrations applied; old client
   blocked by version check; new client works.
6. Walk through `07-go-live-checklist.md` and tick every item.

## 12. Acceptance criteria

- [x] API runs as a Windows Service with auto-migrate and startup checks; HTTPS option documented
      (service registration itself verified only in the clean-VM smoke test, § 15).
- [ ] Server and client installers produce a working system on clean machines — scripts written, not yet compiled
      or run (§ 15).
- [x] Scheduled and manual backups work; restore procedure executed successfully in rehearsal (development
      database, 2026-10-14).
- [x] Logs, health, version compatibility and server notices in place.
- [ ] Ops documentation complete (`docs/ops/01`–`07`); load test passed on the development PC, still to be run
      on target hardware.
- [ ] Definition of Done satisfied; project status table marked complete.

## 13. Risks and notes

- PostgreSQL has no edition size limit; audit log growth is the main driver of database size
  (retention setting from Phase 9). Autovacuum must stay enabled.
- Self-signed HTTPS adds support burden; keep HTTP as the default for isolated LANs and HTTPS as a
  documented option.
- Installer development is often underestimated; start the Inno scripts early in this phase and test
  on a clean VM every time.
- (From Phase 9) The server's SignalR comes from the shared ASP.NET Core runtime, which NuGet scans do not see.
  The installer's prerequisite check must require runtime 8.0.32 or later (CVE-2026-70159), or the server must be
  published self-contained with 8.0.32+; the client packages are already pinned at 8.0.32.

## 14. Changes during implementation

**10.1 Hosting and observability (2026-10-14)**

- `UseWindowsService` (service name "HotelPOS API"); as a service the content root is the exe folder.
- Startup checks in `DatabaseInitializer`: waits up to `Database:StartupWaitSeconds` (default 60, template 120) for
  PostgreSQL because at boot its service may start after ours; a missing database is fine (the migration creates
  it); pending migrations with `AutoMigrate` off and missing core settings stop the server. A failed check logs one
  critical line naming database, host and port (never the password) and exits with code 1 so the service recovery
  restarts it.
- `/health` gained a `disk` check (Degraded below `Monitoring:MinFreeDiskMb`, default 1024) over the log, media
  and (10.2) backup folders, and a `problems` map with the reason; exception text is not exposed because `/health`
  is anonymous. Low disk is also logged at start-up.
- `GET /api/admin/logs` and `POST /api/admin/notice` behind a new permission `server.manage` (Admin only by
  default). `ServerNoticeEvent` gained `level` and `sentBy`.
- `appsettings.Production.template.json`: the installer copies it to `appsettings.Production.json` (gitignored,
  it holds the connection string and JWT key) with logs and media under `C:\ProgramData\HotelPOS\Api`.

**10.2 Backup and restore (2026-10-14)**

- `Backups` table (migration `Phase10_Ops`): file, pictures zip, size, status, trigger, who, start/finish, error.
- `BackupService`: `pg_dump --format=custom --no-owner` (password via `PGPASSWORD`, never on the command line),
  verified with `pg_restore --list`, menu pictures zipped next to it, retention cleanup of files and rows, audit
  `Backup.Completed` / `Backup.Failed`; one backup at a time; rows left `Running` by a server stop are closed as
  interrupted. Tools come from `Backup:PgBinPath`, else PATH, else the newest `Program Files\PostgreSQL\*\bin`.
- `BackupHostedService`: one scheduled attempt per day once `BackupTime` (local) has passed, so a server that was
  off at 03:30 catches up when it starts; a failure is reported, not retried every minute. `Backup:Scheduler=false`
  in test hosts.
- Settings `BackupEnabled`, `BackupTime`, `BackupRetentionDays`, `BackupPath` (empty = `Backup:DefaultPath`,
  `C:\ProgramData\HotelPOS\Backups`); the folder joins the `/health` disk check.
- "Failure raises an admin notice at next login" is met by the Backup screen's last-failure banner (10.3) and the
  audit entry rather than a login-time popup.
- Scripts: `scripts/restore-db.ps1` (verify, stop service, drop/create, `pg_restore`, pictures, start),
  `scripts/backup-now.ps1` (direct `pg_dump` when the API is down), `scripts/create-firewall-rule.ps1`.
  **Rehearsed on 2026-10-14**: the development database was dumped and restored into a scratch database with
  `restore-db.ps1`; all 30 tables had identical row counts.

**10.3 Desktop (2026-10-14)**

- Version gate (`VersionGate`): at start-up (`StartupFlow`) and on the login screen (catches a server upgraded while
  the terminal kept running), a client below `minClientVersion` gets the **Update required** screen with installed,
  required and server versions and CHECK AGAIN. An unreachable server never blocks (the connection handling does).
- **Backup** screen (`backup.manage`): BACKUP NOW (10-minute request timeout), last-failure banner until a newer
  backup succeeds, history grid, and the four backup settings edited in place.
- **Server** screen (`server.manage`, new module): API log newest first with level and size filters; notice
  sender (level + message, confirmation before broadcasting).
- Every terminal shows `ServerNotice` broadcasts as a toast (Info / Warning / Critical as error) with the sender;
  `DEVICE_DISABLED` still signs the terminal out.

**10.4 Publishing and installers (2026-10-14)**

- `build/version.props` is the single version source (imported by `Directory.Build.props`).
- `build/publish.ps1`: tests, self-contained win-x64 publish of server and client into
  `artifacts/publish/<version>/`, scripts copied next to the server, installers built when Inno Setup is present.
  It reads the ASP.NET Core version actually bundled and **refuses a release below 8.0.32** (CVE-2026-70159);
  `-AllowOutdatedRuntime` produces a folder marked `NOT-FOR-RELEASE.txt` for local trials. The current build PC's
  SDK (8.0.417) bundles 8.0.23, so the .NET 8 SDK must be updated before the first venue release.
- `appsettings.Development.json` is excluded from publishing (it holds the development key and demo-data switch).
- `scripts/configure-server.ps1` (run by the server installer, re-runnable): checks PostgreSQL is reachable,
  writes `appsettings.Production.json` from the template (connection string with quoted password, 48-byte random
  JWT key kept on re-runs, admin seed password, port, data folders under `C:\ProgramData\HotelPOS`), restricts it
  to Administrators and SYSTEM, registers service `HotelPOSApi` (automatic, restart on failure 5 s / 15 s / 60 s),
  firewall rules, starts and waits for `/health`. Passwords reach it in a temporary file it deletes, never on a
  command line.
- `installer/server.iss` (database page, admin password, port; uninstall removes the service but never the data)
  and `installer/client.iss` (`/API=` pre-fills the server address for new terminals, `/CERT=` trusts a self-signed
  HTTPS certificate, ProgramData settings folder writable by users).
- **Verified 2026-10-14**: `publish.ps1` produced both folders (server 372 files, client 506; no development
  settings) and the runtime check reported 8.0.23 as intended. The published server, configured by
  `configure-server.ps1 -ConfigOnly` against a new database, created and migrated it (all 8 migrations), seeded the
  admin with the installer password (must change at first sign-in), answered `/health` Healthy in Production mode
  and did not expose Swagger. **Not yet verified**: compiling and running the two `.iss` scripts (Inno Setup is not
  installed on the build PC) and the service registration path (needs an elevated session) — both belong to the
  clean-VM smoke test in § 10.

**10.5 Operations documentation and load test (2026-10-14)**

- `docs/ops/01`–`07` for the venue technician: installation, LAN and firewall (fixed address, Private profile,
  port checks, optional HTTPS), backup and recovery (restore and rehearsal), upgrade, troubleshooting table, daily
  and weekly routine, signed go-live checklist.
- `tools/HotelPOS.LoadTest`: N virtual waiters run order → kitchen start/ready → serve → request bill → finalize
  → payment in a loop against a running API, while a cashier hub connection counts `BillRequested` events; reports
  p50/p95/max per step and fails when a p95 exceeds the budget, a request fails or an event is lost.
  **Result on the development PC, 2026-10-14** (API, PostgreSQL 18 and the tool on one machine, scratch database):
  20 clients for 3 minutes, 11,820 complete cycles, slowest p95 58 ms (submit order), no errors, 11,820 of
  11,820 events delivered.
- Found while writing the operations guide: a deactivated user kept working until the access token expired; fixed
  (see Phase 9 § 14).

## 15. Remaining work (open)

- [x] Update the .NET 8 SDK on the build PC so the self-contained publish bundles ASP.NET Core 8.0.32 or later (done 2026-10-16: SDK 8.0.426, bundle 8.0.32; both installers built with Inno Setup 6.7.3)
      (the current SDK bundles 8.0.23 and `publish.ps1` refuses it for a release).
- [ ] Install Inno Setup 6, build both installers with `publish.ps1`, and run the installer smoke test on clean VMs
      (§ 10): server installer, service registration and restart-on-failure, client installer with `/API=`,
      full order-to-payment flow, restore on a second VM.
- [ ] Run the load test on venue-like hardware for the full 10 minutes over Wi-Fi.
- [ ] Manual demo script (§ 11) and the go-live checklist on site.
