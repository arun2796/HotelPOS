# Phase 26 — Embedded PostgreSQL

Status: code complete 2026-10-26 (installer, scripts, publish; sandbox install test in progress). Owner's ask of
2026-10-25: "without server, any other option with simply setup?" The server stays (the terminals share one live
state); the separate PostgreSQL installation goes.

## 1. Goal

A venue PC becomes a HotelPOS server with **one installer and one wizard**: `HotelPOS-Server-Setup-<version>.exe`
carries PostgreSQL, creates the database cluster and its Windows service, and the technician never sees the
PostgreSQL installer, its superuser password or Stack Builder. Installing an existing PostgreSQL stays possible.

## 2. Prerequisites

Phase 10 (installers, `configure-server.ps1`), Phase 23 (service start inside the SCM window, pre-upgrade backup),
Phase 24 (.NET 10). The firewall fallback to `netsh advfirewall` of 2026-10-25 (Windows Sandbox has no
`MSFT_NetFirewallRule` class).

## 3. Scope

In: PostgreSQL binaries inside the server package; a wizard page "PostgreSQL" (bundled / existing); cluster creation,
service `HotelPOSDb`, generated superuser password; `pg_dump`/`psql` discovery in the API and the ops scripts;
uninstall of the service (data stays); docs/ops/01 and 03.

Out: SQLite or any other database (the data layer stays PostgreSQL); a hosted server; migrating an existing
stand-alone PostgreSQL into the bundled one (documented as a backup/restore).

## 4. Deliverables by project

- **installer/server.iss** — `PostgresPage` (`CreateInputOptionPage`, default bundled; an upgrade keeps the current
  mode), `ShouldSkipPage` hides the superuser page when bundled, `"BundledPostgres"` in the handover JSON,
  `[UninstallRun]` stops and deletes `HotelPOSDb`.
- **scripts/configure-server.ps1** — `-BundledPostgres`, `-PgBinDir`, `-PgDataDir`, `-PgServiceName`. New cluster:
  random superuser password → `C:\ProgramData\HotelPOS\pg\postgres-password.txt` (Administrators and SYSTEM only),
  `initdb -U postgres -A scram-sha-256 -E UTF8 --locale=C` run **as NetworkService through a one-off scheduled
  task** (PostgreSQL refuses to run with administrator rights), `listen_addresses='localhost'`, `port`,
  `logging_collector`; `pg_ctl register -N HotelPOSDb -S auto`, restart-on-failure, start, `pg_isready`. Then the
  existing flow (role, database, settings, service). `Backup:PgBinPath` is set to `pgsql\bin`.
- **scripts/new-database.ps1, backup-now.ps1, restore-db.ps1** — look in `..\pgsql\bin` first.
- **src/HotelPOS.Infrastructure/Services/BackupService.cs** — `PostgresTools.Find` probes `pgsql\bin` next to the
  API before PATH and Program Files.
- **build/publish.ps1** — `-PgSource` (default `artifacts\pg-binaries\pgsql`, else the newest PostgreSQL installed on
  the build PC); copies `bin`, `lib`, `share` to `server\pgsql`, drops pgAdmin and Stack Builder, keeps the licence
  text, records the version in `version.txt`.
- **Tests** — `tests/HotelPOS.Application.Tests/Backups/PostgresToolsTests.cs`.

## 5. Data model

None.

## 6. API endpoints

None.

## 7. Real-time events

None.

## 8. Business rules

- The bundled server listens on localhost only; terminals never reach the database (golden rule 1).
- Uninstalling HotelPOS Server removes both services and keeps `C:\ProgramData\HotelPOS` (database, backups, logs).
- An upgrade of a server that uses an existing PostgreSQL keeps using it (the wizard pre-selects "existing").

## 9. Desktop screens

None.

## 10. Tests

- `PostgresToolsTests` (configured folder, missing tool, probing without exceptions).
- Installer: the Windows Sandbox recording (`docs/ui-ux/tools/sandbox/run-sandbox.ps1`) installs the server with
  the bundled PostgreSQL on a clean Windows, then the client, and signs in: this is the clean-VM install test of
  docs/ops/10 as well.

## 11. Manual demo script

1. Clean Windows PC or sandbox: run `HotelPOS-Server-Setup-<version>.exe`, keep "Install the bundled PostgreSQL",
   choose the hotelpos password and the admin password, leave HTTPS empty, Install.
2. `services.msc`: `HotelPOSDb` and `HotelPOS API` running; `http://localhost:5000/health` says Healthy.
3. Administration → Backup → BACKUP NOW works (pg_dump from `pgsql\bin`).
4. Uninstall: both services gone, `C:\ProgramData\HotelPOS\pg\data` still there.

## 12. Acceptance criteria

- [x] Server package carries PostgreSQL; `version.txt` names its version.
- [x] Wizard: PostgreSQL page, superuser page skipped when bundled.
- [x] `configure-server.ps1` creates cluster and service without any PostgreSQL on the PC.
- [ ] Sandbox install test passes end to end (server healthy, client signs in).
- [x] Backups find `pg_dump` without configuration.
- [x] docs/ops/01 and 03 updated.

## 13. Risks and notes

- Installer size grows by about 50 MB (bin + lib + share, LZMA2).
- The PostgreSQL binaries come from the build PC's installation (or an EDB binaries zip in `artifacts\pg-binaries`);
  the version bundled is whatever that is, printed at publish time.
- `initdb` as NetworkService needs `schtasks`; on a PC where the Task Scheduler service is disabled the install
  fails with a clear message and the technician can install PostgreSQL by hand (option 2).

## 14. Changes during implementation

- 2026-10-26: Windows Sandbox showed that PostgreSQL's installer and Inno Setup expose nothing to UI Automation and
  that the sandbox lacks the firewall WMI class; both findings shaped the install scripts (netsh fallback) and the
  recording tool.
