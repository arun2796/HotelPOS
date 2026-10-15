# 04 — Upgrade

Upgrade outside service hours. Allow 30 minutes; the database part usually takes seconds.

## 1. Before

1. Read the release notes for the new version: anything to do by hand is listed there.
2. Warn the staff: **Administration → Server → Send notice** ("Server restarting in 10 minutes for maintenance").
3. Make sure no bill is half paid; finish or release open bills on the billing counters.

## 2. Server

1. **Backup now** in the app (or `scripts\backup-now.ps1` if the API is already stopped). Copy it to a USB drive.
2. Run the new `HotelPOS-Server-Setup-<version>.exe` as administrator. Leave the database password and admin
   password **empty** on the wizard pages: the existing settings and token key are kept.
3. The installer restarts the service. On start the API applies any database migrations by itself, then reports
   healthy. Check `http://localhost:5000/health` and the top of today's log (Administration → Server).

## 3. Terminals

**Automatic (from Phase 16):** the server installer also places the client installer in the server's `updates`
folder (with `latest.json`). A terminal that runs an older version shows **Update now** in the banner at the top; the
cashier or waiter taps it when convenient (about a minute, Windows asks for administrator permission once), and
HotelPOS restarts on the new version. Check a terminal's version in the status bar (bottom right) or under
Administration → Devices. If a terminal cannot update itself, install by hand as below.

**By hand:** install the new `HotelPOS-Client-Setup-<version>.exe` on each terminal; their settings are kept. When a release
raises the minimum client version, terminals still on the old version show **Update required** instead of the
sign-in screen until they are updated, so no old terminal can talk to the new server by mistake.

## 4. If something goes wrong

- The service does not start: see the newest log in `C:\ProgramData\HotelPOS\Api\logs`. The first lines after
  "HotelPOS API cannot start" name the problem (database unreachable, pending migrations, missing settings).
- To go back: reinstall the previous server version, then restore the backup taken in step 2.1 with
  `restore-db.ps1` (the old program cannot read a database migrated by the new one).
