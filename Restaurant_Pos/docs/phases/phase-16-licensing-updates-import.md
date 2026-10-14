# Phase 16 — Licensing, Auto-update and Import

Status: In progress (started 2026-10-15) · Depends on: Phases 10, 12, 13, 14 · Reference: BillingHub `LicenseService`,
`Shared/Licensing`, `BillingSoftware.LicenseGenerator`, `VelopackUpdateService`, `ImportService` (ideas only)

## 1. Goal

Sell and maintain HotelPOS like BillingHub: an offline, machine-locked licence on the server (with the licensed
number of terminals and a trial), terminals that update themselves to the server's version over the LAN, and
Excel/CSV import of the menu and customers with preview and undo.

## 2. Prerequisites

- Phase 10 (version gate `minClientVersion`, `publish.ps1`, installers), Phase 12 (MediatR pipeline), Phase 14
  (ClosedXML), Phase 9 (permissions, audit).

## 3. Scope

**In**
- Licence: ECDSA P-256 signed key bound to the server's machine id, with edition, terminal count and expiry; 7-day
  trial from the first start of a new database; vendor key generator (console) outside the shipped product.
- Auto-update: the server hosts the client installer under `/updates/`; a terminal below the server's latest version
  downloads it (checksum verified) and runs it.
- Import: menu (categories, items, prices, taxes, stations, modifiers) and customers from Excel/CSV with column
  mapping, validation preview, apply in one transaction, undo of the last import.

**Out**
- Online activation, payment collection, a vendor UI, delta updates, feature gating by edition (the edition is
  recorded and shown only).

## 4. Deliverables by project

| Project | Deliverables |
|---|---|
| Domain | `Licensing` (`LicensePayload`, `LicenseKeyCodec` sign/verify, `MachineIds`, `LicenseEvaluator`, `ServerLicense`), `ImportBatch` / `ImportEntry`, `Customer` |
| Application | `Features/Licensing` (`LicenseService`, status query, apply command), `Features/Import` (field maps, planner, executor, preview/apply/undo/last), login checks, update offer in `GetSystemInfo` |
| Infrastructure | EF configuration, migration `Phase16_LicensingImport`, `HardwareMachineIdProvider`, `TabularReader` (ClosedXML + CSV), `ClientUpdateSource` |
| Api | `LicenseController`, `ImportController`, `LicenseEnforcementMiddleware`, `/updates/*` static files |
| Contracts | licence, import and update DTOs, permissions `license.manage` / `import.run`, error codes |
| Desktop | Licence screen (status, machine id, apply key), banner, Import wizard, `UpdateService` and "Update now" on the Update required screen |
| Tools | `tools/HotelPOS.LicenseGenerator` (keygen, issue, inspect) |
| Build | `publish.ps1` places the client installer and `latest.json` in `server\updates` |

## 5. Data model

Migration `Phase16_LicensingImport`.

- `ServerLicense` (single row, Id 1): `TrialStartedAt`, `Key` (null until applied), `AppliedAt`, `AppliedBy`. The
  migration inserts the row, so the trial starts when the database is first migrated.
- `Customers` (Id, Name, Phone unique when set, Email, Gstin, LegalName, StateCode, Address, IsActive).
- `ImportBatches` (Id, Kind, FileName, Status Applied|Undone, Created/Updated counts, AppliedAt/By, UndoneAt/By) and
  `ImportEntries` (BatchId, Seq, EntityType, EntityId, Action Created|Updated, Before JSON).

## 6. API endpoints

| Endpoint | Who | Notes |
|---|---|---|
| `GET /api/license` | any signed-in user | status (state, edition, terminals used/allowed, expiry, machine id) |
| `POST /api/license/apply` | `license.manage` | `{ key }`; verifies signature, machine id and expiry |
| `POST /api/import/preview` | `import.run` | multipart `file`, `kind`, optional `mapping` JSON; headers, suggested mapping, row errors |
| `POST /api/import/apply` | `import.run` | same form plus `skipInvalid`; one transaction |
| `POST /api/import/undo` | `import.run` | undoes the last applied import |
| `GET /api/import/last` | `import.run` | the last batch and whether it can be undone |
| `GET /updates/latest.json`, `/updates/<file>` | anonymous | static; `GET /api/system/info` also carries the latest client version |

Error codes: `LICENSE_TERMINAL_LIMIT` (403), `LICENSE_EXPIRED` (403), `LICENSE_INVALID` (400), `IMPORT_HAS_ERRORS` (422).

## 7. Real-time events

`MenuChanged` after a menu import or its undo (existing event). No new events.

## 8. Business rules

**Key format**: `HPOS1-<base64url payload JSON>.<base64url ECDSA P-256/SHA-256 signature>`. Payload: licence id,
customer, machine id, edition, terminals, issued date, expiry date (null = perpetual). The server embeds only the
public key; the private key is created by the generator on the vendor's PC and is never in the repository.

**Machine id**: `HP-` + 20 hex of SHA-256("HotelPOS|" + Windows MachineGuid (or /etc/machine-id) + processor id).

**States**: *Trial* (7 days, 10 terminals) → *Licensed* → *Expired* (licence past expiry, or trial over). A stored
key that no longer verifies for this PC (database copied elsewhere) counts as Expired.

**Terminals**: the active devices ordered by registration date; the first N (licence terminals) may sign in. A new
terminal name over the limit, or one beyond the first N after the limit was lowered, is refused at login with
`LICENSE_TERMINAL_LIMIT` and is not registered. Logins without a device name are not counted.

**Expiry (decision)**: *read-only, not locked out.* When Expired: every write (non-GET) is refused with
`LICENSE_EXPIRED` except `/api/auth/*`, `/api/license/*` and `/api/admin/backup`; reads and exports keep working;
only users with `license.manage` (Admin) can sign in, so they can see the data, back up and apply a new key. Banner
from 14 days before expiry and during the trial.

**Update**: `latest.json` = `{ version, file, sha256, size }`. A terminal whose version is below `latestClientVersion`
offers "Update now": download to `%TEMP%`, verify SHA-256, run the Inno Setup installer silently
(`/SILENT /CLOSEAPPLICATIONS`) and exit. A terminal below `minClientVersion` is blocked by the Phase 10 gate and gets
the same button. No third-party updater: the server already serves the installer, and the Phase 10 installers and
service model stay unchanged.

**Import**: kinds `Menu` (Category, Item, Price required; Tax, Station, Code, Description, HSN/SAC, Modifiers
optional) and `Customers` (Name required; Phone, Email, GSTIN, Legal name, State, Address). Matching: category,
station and modifier group by name; tax by name or rate; item by (category, name); customer by phone, else GSTIN.
Existing rows are updated, others created. Modifiers cell: `Group: Option +delta, Option; Group2: Option`. Row errors
(missing/invalid value, duplicate in the file, bad GSTIN) are listed with row and column; apply refuses while errors
exist unless `skipInvalid`. Everything is written in one transaction with before-images per touched row. Undo only
reverts the latest applied batch: created rows are deleted (deactivated when already used), updated rows restored.
Limit: 5,000 rows, 5 MB.

## 9. Desktop screens

Licence (Administration; status card, machine id, apply-key box), top banner (trial/expiry near), Import (wizard:
file → map columns → preview errors → apply → undo last).

## 10. Tests

Licence sign/verify/tamper, machine binding, trial/expiry evaluation, generator round trip, terminal limit at
login, expiry read-only, import mapping/validation/apply/undo (500 items), update check logic, API matrix.

## 11. Manual demo script

1. Fresh server: Licence screen shows Trial, 7 days. 2. Issue a key with the generator for the shown machine id;
apply it; status Licensed. 3. Sign in an eleventh terminal: refused. 4. Import a 500-item sheet; undo. 5. Publish a
new version; a terminal offers the update and restarts on it.

## 12. Acceptance criteria

- [x] Trial → licensed key on a clean server; terminal limit enforced; client auto-updates from the server (download,
      checksum and installer start are tested; running it on a real terminal belongs to the manual demo).
- [x] Menu import of 500 items with preview and undo; Definition of Done satisfied.
- [ ] Manual demo (§ 11) on real machines.

## 13. Risks and notes

- The trial and the key live in the database: restoring an old backup restores the old trial date. Accepted for an
  offline product; the key is machine-bound, so copying a database to another PC invalidates it.
- Updating needs the installer's admin rights (UAC prompt on the terminal).

## 14. Changes during implementation

**Built (2026-10-15, branch `phase-16-licensing-updates-import`)**

- Domain/Contracts: `LicenseKeyCodec` (sign/verify), `MachineIds`, `LicenseEvaluator`, `ServerLicense`, `Customer`,
  `ImportBatch`/`ImportEntry`; permissions `license.manage` (Admin) and `import.run` (Admin, Manager); error codes
  `LICENSE_TERMINAL_LIMIT`, `LICENSE_EXPIRED`, `LICENSE_INVALID`, `IMPORT_HAS_ERRORS`.
- Application: `LicenseService` (login and device registration check the terminal limit; login refuses non-admins when
  expired), import planner/executor/requests, `clientUpdate` in `GetSystemInfo`. Infrastructure: migration
  `Phase16_LicensingImport` (tables, trial row, permission grants), `HardwareMachineIdProvider`, `TabularReader`
  (ClosedXML + CSV), `ClientUpdateSource`. Api: `LicenseController`, `ImportController`, `LicenseEnforcementMiddleware`,
  `/updates/` static files.
- Tool: `tools/HotelPOS.LicenseGenerator` (keygen/issue/inspect; refuses to write a key inside a git repository);
  `.gitignore` ignores `*.pem`, `license-private*`, `*.hpkey`. The embedded public key is a real one; its private key
  is in `C:\Users\Arunkumar\.hotelpos-vendor\license-private.pem` (outside the repository: back it up).
- Desktop: Licence and Import screens (registered, permission-gated), banner above every screen, `UpdateService`
  with "Update now" on the banner and the Update required screen; `client.iss` starts the new version after a
  silent `/RESTART=1` install; `build/publish.ps1` builds the client installer first, copies it with `latest.json`
  (version, file, SHA-256, size) to `server\updates`, then builds the server installer. `ScreenRenderingTests` renders
  70 screens, no binding errors. No NuGet package added (no NU1701 risk, no new vulnerability scan needed).
- Authorization matrix: 6 added lines, nothing else changed: `api/import/apply`, `/last`, `/preview`, `/undo` are
  `AM---`; `api/license` (GET) is `AMWKC`; `api/license/apply` is `A----`.
- Tests added: 15 domain (incl. generator round trip), 18 application (8 licence, 10 import incl. 500 items and undo),
  4 API classes, 15 desktop (update check/download/checksum, banner text, licence and wizard view-models) + 3 rendering screens.

Full run: Domain 365, Application 224, Desktop 232 (1 skipped, pre-existing) all green; Api 175 of 176 (the SignalR
test `DeactivatingDevice_ClosesItsOpenHubConnection...` timed out under load and passed on its own, 11 of 11 in its class).

**Decisions and deviations**

- **Expiry is read-only, not a lock-out**: reads and exports keep working, writes answer `LICENSE_EXPIRED` (except
  auth, licence and backup), and only administrators can sign in. Please confirm this is the policy you want.
- **Terminal count** = registered active devices, first N by registration date may sign in; a new name over the limit
  is refused before it is registered. Trial: 7 days, 10 terminals, started by the migration.
- **Update mechanism**: server-hosted installer + checksum + Inno silent install, instead of Velopack (no extra
  dependency, reuses the Phase 10 installers and version gate). A terminal must be restarted by the installer, so
  Windows asks for administrator permission each time.
- Machine id = Windows MachineGuid + processor id: reinstalling Windows or swapping the CPU needs a new key.
- There was no customer master, so `Customers` was added for the customer import; nothing reads it yet (bills keep
  their own snapshot).
- Phone numbers are stored as 10 national digits (+91 / leading 0 dropped) so re-imports match.
- Undo only reverts the latest batch, once; created rows already used by orders are deactivated, not deleted.

**Not done / deferred**

- Manual demo (§ 11), including a real publish with Inno Setup and an actual terminal self-update.
- Customer picker on the bill / customer list screen; the vendor UI (console only); edition-based feature gating.
- The trial/licence can be reset by restoring an old database backup (documented in § 13).
