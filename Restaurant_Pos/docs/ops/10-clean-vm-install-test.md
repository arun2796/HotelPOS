# 10 — Clean-VM install test

Run this once per release before the installers go to a venue. It proves that the two setup files install and run on
a Windows PC that has never seen HotelPOS or a developer tool. It is the open item of Phase 10 § 15 and takes about
one hour with a prepared VM.

Release: __________  Tester: ____________________  Date: __________  Result: PASS / FAIL

## 0. What you need

- [ ] Two clean Windows 10/11 Pro x64 VMs (Hyper-V, VirtualBox or VMware), 4 GB RAM and 30 GB disk each, named
      `POS-SERVER` and `POS-CLIENT`, on the same virtual network, network profile **Private** on both. One VM is
      enough if you take a snapshot after step 1 and use it again for § 6.
- [ ] No .NET SDK or runtime, no PostgreSQL, no Visual Studio installed on either VM. Take a snapshot of each VM
      before you start so the test can be repeated.
- [ ] From the build PC: `artifacts\installers\HotelPOS-Server-Setup-<version>.exe`,
      `HotelPOS-Client-Setup-<version>.exe` and `artifacts\publish\<version>\version.txt`. Copy them to
      `C:\Install` on the VMs (shared folder or ISO). `version.txt` must say ASP.NET Core **10.0.8 or later** and there
      must be no `NOT-FOR-RELEASE.txt` next to the installers.
- [ ] The PostgreSQL 16 Windows installer (EDB) copied to `C:\Install` on `POS-SERVER`.
- [ ] A licence key for the server VM is **not** needed: the 7-day trial covers the test.

## 1. PostgreSQL on POS-SERVER

- [ ] Install PostgreSQL 16, default port 5432, superuser password noted on this sheet only.
- [ ] In *SQL Shell (psql)* as `postgres`:

  ```sql
  CREATE ROLE hotelpos LOGIN PASSWORD 'Test@12345';
  CREATE DATABASE hotelpos OWNER hotelpos;
  ```

- [ ] Snapshot the VM here (`after-postgres`).

## 2. Server installer on POS-SERVER

- [ ] Run `HotelPOS-Server-Setup-<version>.exe` as administrator. Database page: `localhost`, `5432`, `hotelpos`,
      `hotelpos`, `Test@12345`, API port `5000`. Administrator page: `Admin@Test1`. HTTPS page: leave both fields
      empty (automatic LAN certificate).
- [ ] The wizard finishes without an error dialog. Note the time it took: ______ min.
- [ ] `services.msc`: **HotelPOS API** is *Automatic* and *Running*; Recovery tab shows restart on failure.
- [ ] `http://localhost:5000/health` says `"status":"Healthy"`; `https://localhost:5001/health` opens after the
      browser warning (or without a warning once the root CA from step 3 is installed).
- [ ] Folders exist: `C:\Program Files\HotelPOS\Server`, `C:\ProgramData\HotelPOS\Api\logs`,
      `C:\ProgramData\HotelPOS\Api\hotelpos-root-ca.cer`, `C:\ProgramData\HotelPOS\Backups`.
- [ ] `appsettings.Production.json` cannot be opened by a non-administrator user (right-click → Properties →
      Security shows only Administrators and the service account).
- [ ] Windows Defender Firewall → Inbound rules: HotelPOS rules for 5000 and 5001 exist (Private); PostgreSQL 5432
      is blocked from the network.
- [ ] Open today's log in `C:\ProgramData\HotelPOS\Api\logs`: migrations applied, no `ERROR` lines.

## 3. Client installer on POS-CLIENT

- [ ] Find the server VM's address: `ipconfig` on `POS-SERVER` → `__________`.
- [ ] From `POS-CLIENT`: `Test-NetConnection <server> -Port 5000` succeeds, `-Port 5432` **fails**.
- [ ] Install the root CA on `POS-CLIENT` (copy `hotelpos-root-ca.cer`, double-click → Install → Local Machine →
      Trusted Root Certification Authorities).
- [ ] Interactive install: run `HotelPOS-Client-Setup-<version>.exe` as administrator, defaults. The app starts
      and asks for the server address and a terminal name. Enter `https://<server>:5001`, `BILLING-01`, type
      Billing.
- [ ] The sign-in screen shows 🟢. Sign in as `admin` / `Admin@Test1`; the forced password change appears; set
      `Admin@Test2`.
- [ ] Silent install check: on `POS-SERVER` itself run
      `HotelPOS-Client-Setup-<version>.exe /VERYSILENT /SUPPRESSMSGBOXES /API=https://<server>:5001`, start the app,
      the address is pre-filled; name it `ADMIN-01`, type Admin, sign in.
- [ ] Administration → Devices lists `BILLING-01` and `ADMIN-01`; Administration → Licence shows the trial with
      its end date.

## 4. Order to payment on the clean install

Do this on `ADMIN-01` unless a step names another terminal. Use the seed menu; add one item if the menu is empty.

- [ ] Administration → Restaurant: name, address, GSTIN, invoice prefix saved.
- [ ] Floor: one section with two tables; Menu: one category, one item with a tax, one station.
- [ ] Users: create `waiter1` (Waiter) and `cashier1` (Cashier).
- [ ] Sign in as `waiter1`, take table T1, order the item, submit. The KOT prints to the Windows default printer
      or shows in the print log as failed with a clear reason (no printer on a VM is expected).
- [ ] Kitchen screen: ticket appears, mark ready; the waiter sees "order ready"; serve.
- [ ] On `BILLING-01` sign in as `cashier1`, open a shift with a float, request the bill for T1, apply a 10 %
      discount, pay by cash, finalize. Invoice number `__________`.
- [ ] Reports → Sales for today shows the bill; Dashboard KPI shows the amount.
- [ ] Administration → Backup → **BACKUP NOW** succeeds; a `.dump` file appears in `C:\ProgramData\HotelPOS\Backups`.
      Copy it to `C:\Install` on `POS-CLIENT` for § 6.
- [ ] Administration → Server → Send notice: the notice appears on `BILLING-01` within 5 s.

## 5. Service recovery

- [ ] Task Manager on `POS-SERVER`: end the `HotelPOS.Api` process. Within 1 min the service is running again
      and `/health` is Healthy; terminals showed 🔴 then 🟢; no sign-in needed.
- [ ] Unplug the server VM's virtual network cable for 30 s during an open order on `BILLING-01`. After reconnect
      the order is there exactly once.
- [ ] Restart `POS-SERVER`. The service comes back by itself; both terminals reconnect.
- [ ] `/health` after the restart reports disk and database OK.

## 6. Restore on a second VM

Use `POS-CLIENT` (or the `after-postgres` snapshot of the server VM) as a stand-in for a replacement server.

- [ ] Install PostgreSQL and the role and database as in § 1.
- [ ] Run the server installer as in § 2, with the same database password and a new admin password.
- [ ] In an elevated PowerShell:

  ```powershell
  & 'C:\Program Files\HotelPOS\Server\scripts\restore-db.ps1' -BackupFile 'C:\Install\<file>.dump'
  ```

  The script verifies the file, stops the service, drops and recreates the database, restores it and the pictures, and starts the service again.
- [ ] Sign in as `admin` with the **original** password `Admin@Test2` (the restore brings the users with it).
- [ ] Reports show the § 4 bill with the same invoice number; the menu and tables are there.
- [ ] Administration → Licence: the machine id is different from the first server; the trial still runs
      ([08 § 3](08-licensing.md) covers moving a licensed server).

## 7. Upgrade in place (optional, needs two builds)

- [ ] Run a newer server installer on `POS-SERVER` with the database and admin fields left **empty**. The service
      restarts, migrations apply, `/health` is Healthy, settings and token key are kept (terminals stay signed in).
- [ ] `BILLING-01` shows **Update now** in the banner; the update installs and the status bar shows the new version.

## 8. Record the result

- [ ] Fill in the result line at the top. Attach screenshots of `/health`, `services.msc`, Devices, and the invoice.
- [ ] On FAIL: copy `C:\ProgramData\HotelPOS\Api\logs\*.log` from the server VM, the installer log
      (`%TEMP%\Setup Log *.txt`) and the exact wizard page where it failed, and open an issue against the release.
- [ ] Revert both VMs to their first snapshot so the sheet can be run again for the next release.
- [ ] Tick the matching item in `docs/phases/phase-10-production-readiness.md` § 15 with the date and release.
