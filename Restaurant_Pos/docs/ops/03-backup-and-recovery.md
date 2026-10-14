# 03 — Backup and recovery

## 1. What is backed up

| File | Content |
|---|---|
| `hotelpos-<yyyyMMdd-HHmmss>.dump` | The whole database (orders, bills, payments, menu, users, settings, audit log), PostgreSQL custom format |
| `hotelpos-<...>-media.zip` | Menu pictures (only when there are any) |

Every backup is checked with `pg_restore --list` before it is marked OK. Logs and program files are not backed up
(the installer recreates them).

## 2. Schedule

Set in the app under **Administration → Backup** (Admin only):

| Setting | Default | Notes |
|---|---|---|
| Daily at | 03:30 | Local time, before the business day starts. A server that was off at that time takes the backup when it starts. |
| Keep for | 14 days | Older backups and their pictures are deleted after each successful backup. |
| Folder | `C:\ProgramData\HotelPOS\Backups` | Must be on the server. |
| Daily backup enabled | on | |

The Backup screen shows every backup and a red banner when the latest one failed. Failures are also in the audit
log (`Backup.Failed`) and the server log.

## 3. Keep a copy off the server

A backup on the same disk does not survive a disk failure, theft or fire. At least weekly copy the newest `.dump`
and `-media.zip` to a USB drive or NAS that is stored elsewhere. Rotate two USB drives if possible.

## 4. Taking a backup by hand

- In the app: **Administration → Backup → BACKUP NOW**.
- With the API stopped (e.g. right before an upgrade), on the server in PowerShell:

  ```powershell
  & 'C:\Program Files\HotelPOS\Server\scripts\backup-now.ps1'                     # asks for the hotelpos password
  & 'C:\Program Files\HotelPOS\Server\scripts\backup-now.ps1' -Folder 'E:\HotelPOS' # straight to a USB drive
  ```

## 5. Restoring

Restoring **replaces** the whole database with the backup. Everything entered after that backup is lost, so take a
fresh backup first if the current database is still readable.

1. Copy the `.dump` (and `-media.zip` if you also want the pictures back) to the server.
2. Open an elevated PowerShell and run:

   ```powershell
   & 'C:\Program Files\HotelPOS\Server\scripts\restore-db.ps1' -BackupFile 'E:\HotelPOS\hotelpos-20261014-033000.dump'
   ```

   It asks for the `postgres` password, verifies the file, stops the HotelPOS API service, drops and recreates the
   `hotelpos` database (owner `hotelpos`), restores the data, restores the pictures (the current ones are kept in a
   `menu-before-restore-...` folder) and starts the service.
3. Open `http://localhost:5000/health`, sign in and check the latest bills.
4. Terminals reconnect by themselves; terminals that were signed in must sign in again.

### Restoring onto a new server

Install PostgreSQL and the HotelPOS server as in [01](01-installation.md) (same role and database names), then
run `restore-db.ps1` as above. The token key is new, so everyone signs in again; the data is complete.

## 6. Rehearsal (do this at go-live and twice a year)

Restore the latest backup into a scratch database without touching the live one:

```powershell
& 'C:\Program Files\HotelPOS\Server\scripts\restore-db.ps1' -BackupFile <newest .dump> -Database hotelpos_restoretest -SkipService -SkipMedia
```

Compare a few numbers with the live system (e.g. `SELECT count(*) FROM "Bills";` in both databases with psql),
then drop the scratch database: `dropdb -U postgres hotelpos_restoretest`.
