# 01 — Installation

For the technician setting up HotelPOS at a venue. Allow two hours for a first installation, including the
backup and restore rehearsal in [03](03-backup-and-recovery.md).

## 1. What goes where

| Machine | Runs | Notes |
|---|---|---|
| **Server PC** (one) | PostgreSQL, the HotelPOS API as a Windows Service | Always on, wired to the router, static IP |
| **Terminals** (waiter tablets, kitchen screens, billing counters, admin PC) | HotelPOS desktop app | Windows 10/11 x64, on the venue Wi-Fi / LAN |

The server PC may also be used as the admin or billing terminal.

## 2. Server PC requirements

- Windows 10/11 Pro or Windows Server 2019+, x64, 8 GB RAM, SSD with at least 20 GB free.
- A UPS is strongly recommended: a power cut during a write is the main risk to the database.
- Network profile set to **Private** (see [02](02-lan-and-firewall.md)).
- No .NET install is needed: the server and the app carry their own runtime.

## 3. Install PostgreSQL

1. Install PostgreSQL 16 or later (the EDB Windows installer). Keep the default port 5432. Write down the password
   of the `postgres` superuser and keep it with the venue's documents, not on the PC.
2. Open **SQL Shell (psql)** as `postgres` and create the role and database HotelPOS uses (pick a strong
   password; it goes into the HotelPOS installer in the next step):

   ```sql
   CREATE ROLE hotelpos LOGIN PASSWORD '<strong password>';
   CREATE DATABASE hotelpos OWNER hotelpos;
   ```

   The API never needs the superuser. The installer checks that PostgreSQL answers before it continues.

## 4. Install the server

1. Run `HotelPOS-Server-Setup-<version>.exe` as administrator.
2. **Database** page: server `localhost`, port `5432`, database `hotelpos`, user `hotelpos`, the password from
   step 3. API port: `5000` unless something else uses it.
3. **Administrator** page: the first admin password. It is used once, when the admin account is created, and must
   be changed at the first sign-in.
4. Finish. The installer writes the settings, registers the **HotelPOS API** service (starts automatically,
   restarts itself after a failure), opens the firewall for the API port on Private networks, blocks PostgreSQL
   from the network, starts the service and waits until it reports healthy.
5. Check: open `http://localhost:5000/health` on the server. It should say `"status":"Healthy"`.

What the installer created:

| Path | Content |
|---|---|
| `C:\Program Files\HotelPOS\Server` | Program, `appsettings.Production.json` (database password and token key; readable by Administrators and the service only), `scripts\` |
| `C:\ProgramData\HotelPOS\Api\logs` | API logs, 30 days |
| `C:\ProgramData\HotelPOS\Api\media` | Menu pictures |
| `C:\ProgramData\HotelPOS\Backups` | Daily backups |

If the installer reports that the service could not be configured, run
`C:\Program Files\HotelPOS\Server\scripts\configure-server.ps1` in an elevated PowerShell: it prints the reason.
The same script is how you change the database password later (`-DbPassword`).

## 5. Install the terminals

Run `HotelPOS-Client-Setup-<version>.exe` as administrator on each terminal. To roll out many terminals without
typing the address on each, pass the server address (and, for HTTPS, the certificate):

```powershell
HotelPOS-Client-Setup-0.1.0.exe /VERYSILENT /SUPPRESSMSGBOXES /API=http://192.168.1.100:5000
```

On first start the app asks for the server address (pre-filled with `/API=`) and a **terminal name** such as
`WAITER-01`, `KITCHEN-01`, `BILLING-01`, and its type. Kitchen screens can be bound to a preparation station later
in **Administration → Devices**.

## 5a. Phones and tablets (browser client, Phase 18)

Waiters on phones and the kitchen on tablets use the browser client instead of an installer. Per device:

1. Install the root CA certificate created in [02 § 5](02-lan-and-firewall.md) (HTTPS is required).
2. Open `https://<server>:5001/app` and add it to the home screen (Chrome: *Add to Home screen*; Safari: Share →
   *Add to Home Screen*). The icon opens full-screen like an app.
3. On the sign-in screen choose **Waiter** or **Kitchen** for the device; it registers itself as `WEB-WAITER-xxxxxx` /
   `WEB-KITCHEN-xxxxxx` and appears under Administration → Devices, where it can be renamed or deactivated.
4. Kitchen tablets: pick the preparation station on the *Station* tab once; only that station's tickets are shown.
5. Printing stays on the Windows PCs: the kitchen PC prints the KOTs, the billing PC the invoices.

Android phones and tablets can use the **HotelPOS Staff** app instead of the browser: same screens, an app icon, full
screen and a remembered server address, no certificate needed on plain HTTP. See [09](09-android-app.md).

Cashiers, managers and administrators keep the Windows client (installer above).

## 6. First sign-in and setup

1. On the admin terminal sign in as `admin` with the installer's password and set a new one.
2. **Restaurant Settings**: name, address, GSTIN, invoice prefix, business-day start time.
3. **Users**: one account per person (never shared). **Sections & Tables**, **Menu**, **Discounts & Payments**.
4. **Backup**: check the folder and time; press **BACKUP NOW** once and confirm it shows OK.
5. Walk through [07 — Go-live checklist](07-go-live-checklist.md).
