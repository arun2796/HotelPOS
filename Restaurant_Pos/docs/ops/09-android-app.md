# 09 — HotelPOS Staff: the Android app for waiters and the kitchen

The Android app shows the same screens as the browser client (`http(s)://<server>/app`, see [01 § 5a](01-installation.md)):
Tables, Table details, Order builder and My orders for waiters; Station, Kitchen Display and Completed for the kitchen.
Use the app when staff should have an icon and a full-screen experience without certificates, or when the phones are
the restaurant's own. Cashiers, managers and administrators keep the Windows client.

## 1. What the app needs

- Android 8.0 or newer (phones and tablets).
- The HotelPOS server on the same Wi-Fi. Plain `http://` works; `https://` works when the Phase 18 root CA is
  installed on the device ([02 § 5](02-lan-and-firewall.md)).
- Nothing else: no store account, no internet.

## 2. Install

1. Get `HotelPOS-Staff.apk`: from the server (`http://<server>:5000/updates/HotelPOS-Staff.apk` in the phone's
   browser), or copy it by USB / share from the admin PC (`artifacts\android\<version>\`).
2. Open the file. Android asks once to allow installs from this source (browser or Files app): allow it.
3. Open **HotelPOS Staff**. The first screen asks for the **server address**: enter `http://192.168.1.100:5000`
   (your server's address and port), tap **Test connection** until it says "Connected to <restaurant>", then **Save**.
4. Sign in. Choose **Waiter** or **Kitchen** for this device; it registers itself as `APP-WAITER-xxxxxx` or
   `APP-KITCHEN-xxxxxx` and appears under Administration → Devices on the Windows client, where it can be renamed
   or deactivated. Kitchen tablets then choose their station on the *Station* tab.

The app stays signed in across restarts (the sign-in is kept in the phone's secure storage) until the user signs out,
an administrator deactivates the device, or the session is revoked ("Force re-login" on a role change).

## 3. Update

Install the newer `HotelPOS-Staff.apk` over the old one (same steps as § 2). Settings, the server address and the
sign-in survive. The version is shown on the Server page and under Administration → Devices.

## 4. Build a release (admin PC)

Once: `.\build\install-android-tools.ps1` (installs the .NET Android workload, OpenJDK 17 and the Android SDK).

Each release: `.\build\publish-android.ps1 -UpdatesFolder 'C:\ProgramData\HotelPOS\Api\updates'`
- creates the signing keystore the first time at `%USERPROFILE%\.hotelpos-vendor\android-release.keystore`
  (**back it up with the licence private key**; without it existing installs cannot be updated in place);
- writes `artifacts\android\<version>\HotelPOS-Staff-<version>.apk` and copies it to the server's `/updates` folder.

## 5. Troubleshooting

| Symptom | Cause / fix |
|---|---|
| "No HotelPOS server answered at that address" | Wrong address or port, phone on guest Wi-Fi, or port 5000 blocked: on the server PC the Wi-Fi/LAN profile must be **Private** (the firewall rule does not apply to Public networks) — [02 § 1–2](02-lan-and-firewall.md). Try the same address in the phone's browser. |
| HTTPS address refused | The root CA is not installed on the device, or the certificate does not name the address used ([02 § 5](02-lan-and-firewall.md)). |
| "This licence covers N terminals and M are registered" | Every app install and browser profile is a terminal. Deactivate unused ones under Administration → Devices, or extend the licence. |
| "This device was deactivated by an administrator" | Re-activate it under Administration → Devices. |
| Pictures missing on the Order builder | The item has no picture, or the server address changed: open Server settings on the sign-in screen and test again. |
| Ticket did not arrive on the kitchen tablet | Check the tablet's station (*Station* tab) and the connection pill at the top (Connected / Reconnecting / Offline). |
