# Phase 19 — Android App for Waiter and Kitchen (.NET MAUI Blazor Hybrid)

Status: Not started (specified 2026-10-15) · Depends on: Phase 18 (web pages, services, web auth) · Android only
(decided 2026-10-15; iPhone/iPad keep the browser client; an iOS build can be added later from the same project)

## 1. Goal

Ship the Phase 18 waiter and kitchen screens as an installable Android app (`HotelPOS Staff.apk`) without a second UI:
a .NET MAUI Blazor Hybrid host renders the **same Razor pages** inside a native WebView. Gains over the browser:
an app icon and full screen without HTTPS/PWA requirements, a remembered server address, a secure place for the
refresh token, and a path to native features later (camera, Bluetooth printing). Same backend, same contracts, same
rules; nothing in the API changes.

## 2. Prerequisites

- Phase 18 merged (`main` ≥ `77fdf05`).
- Build PC: .NET workload `maui-android`, Microsoft OpenJDK 17, Android SDK (platform 34, build-tools, platform-tools)
  — installed once by `build/install-android-tools.ps1` (§ 4). An Android phone with USB debugging, or the emulator.
- Owner decision § 13-A (package name and signing key custody).

## 3. Scope

**In**
- `src/HotelPOS.Client.Shared` (new Razor Class Library): the Phase 18 `Pages/`, `Layout/`, `Shared/`, `Services/`
  and `css/` move here unchanged in behaviour; the browser project keeps only its host (`Program.cs`, `index.html`,
  service worker, JS storage, cookie session transport).
- Host abstractions in the shared library: `IClientHost` (server base address, platform name, app version),
  `ISessionTransport` (how sign-in/refresh/logout travel: browser = cookie endpoints, Android = `/api/auth/login`,
  `/refresh`, `/logout` with the refresh token in **SecureStorage**), `IBrowserStorage` (browser = localStorage,
  Android = Preferences + app files).
- `src/HotelPOS.Android` (new, `net8.0-android`, minSdk 26 / target 34): `BlazorWebView` host, first-run **Server**
  page (address, Test connection, saved in Preferences), device identity `APP-WAITER-xxxxxx` / `APP-KITCHEN-xxxxxx`,
  cleartext HTTP allowed for the LAN and user-installed CAs trusted, keep-screen-on while on the Kitchen Display,
  app icon and splash in the Phase 17 green.
- Absolute image URLs (menu pictures) through `IClientHost.ServerBase` for both hosts.
- Packaging: `build/publish-android.ps1` → signed release APK in `artifacts/android/<version>/`, copied to the
  server's `/updates` folder; `docs/ops/09-android-app.md` (install by file, enable "install unknown apps", update).
- Tests: `tests/HotelPOS.Web.Tests` becomes `tests/HotelPOS.Client.Tests` (same tests over the shared library) plus
  `TokenSessionTransport` tests (sign-in stores the refresh token, refresh rotates it, failure clears it).

**Out**
- iOS build, Play Store listing, push notifications, native printing, offline order queue beyond the existing drafts,
  any API/DTO/schema change, cashier/manager/admin screens.

## 4. Deliverables by project

| Project | Deliverable |
|---|---|
| `src/HotelPOS.Client.Shared` (new RCL) | moved pages/services/css; `IClientHost`, `ISessionTransport`, `CookieSessionTransport`, `TokenSessionTransport`; `WebAuthState` uses the transport; `ServiceCollectionExtensions.AddHotelPosClient()` |
| `src/HotelPOS.Web` | host only: `Program.cs` registers `JsBrowserStorage`, `CookieSessionTransport`, `BrowserClientHost`; `index.html` links `_content/HotelPOS.Client.Shared/css/*` |
| `src/HotelPOS.Android` (new MAUI) | `MauiProgram.cs` (BlazorWebView, services: `MauiStorage`, `TokenSessionTransport`, `MauiClientHost`, SecureStorage), `MainPage.xaml`, `Platforms/Android` (manifest: INTERNET, network security config), `Resources/` (icon, splash), `Pages/ServerPage.razor` (first run) |
| `build/install-android-tools.ps1` (new) | installs workload, JDK 17 (winget `Microsoft.OpenJDK.17`), Android SDK via `dotnet build -t:InstallAndroidDependencies` |
| `build/publish-android.ps1` (new) | version from `Directory.Build.props`, signed APK (`-p:AndroidKeyStore=true -p:AndroidSigningKeyStore=…`), copies to `artifacts` and `/updates` |
| `tests/HotelPOS.Client.Tests` | renamed web tests + transport tests |
| Docs | `ops/09-android-app.md`, `ops/01 § 5a` (APK option), `01` and `05` (client table), `08` audit addendum |

## 5. Data model — none.  ## 6. API endpoints — none (the app uses the existing `/api/auth/login|refresh|logout`).

## 7. Real-time events — none new; the hub client is the shared `WebRealtimeClient`.

## 8. Rules the app adds (presentation/security only)

- Server address is required before sign-in; "Test connection" calls `GET /api/system/info` as the desktop does.
- Refresh token lives only in Android `SecureStorage` (Keystore-backed); the access token stays in memory; sign-out
  revokes on the server and clears the store. Uninstalling the app removes both.
- Device name generated once per installation (`APP-WAITER-xxxxxx`); the device appears in Administration → Devices
  like any terminal; deactivation signs the app out on its next call (existing middleware).
- Cleartext HTTP is allowed only so a LAN without certificates works; the ops doc recommends HTTPS with the Phase 18
  CA script, and the app trusts user-installed CAs.
- All business rules stay in the shared pages exactly as in Phase 18 (idempotency keys, drafts, option rules, limits).

## 9. Steps

| Step | Work | Proof |
|---|---|---|
| 19.1 | Install the toolchain; create `HotelPOS.Client.Shared`; move pages/services/css; browser host rebuilt on it; transports split | Web tests green; `/app` still serves; publish still places the client under `wwwroot/app` |
| 19.2 | `HotelPOS.Android` host: BlazorWebView, Server page, storage, token transport, device identity, manifest, icon | Debug APK runs on the emulator/phone: sign in, Tables, order, Kitchen Display live via SignalR |
| 19.3 | Signing key, `publish-android.ps1`, `/updates` copy, `ops/09`, status tables | Signed release APK installs on a phone from a file; version shown in the app |
| 19.4 | Device demo (§ 11) | Owner's phone and tablet |

## 10. Tests

- Shared library tests unchanged (23) + transport tests (3–4) + a bUnit smoke test that the Server page validates the
  address like the desktop (`http(s)://host[:port]`, no query/fragment).
- Browser host: existing Api hosting tests. Android: no automated UI tests (emulator in CI is out of scope); the
  Release build of the APK is part of `publish-android.ps1` so a broken host fails the script.

## 11. Manual demo script

1. Install `HotelPOS Staff.apk` on the phone (file from `/updates` or USB) → first screen asks for the server; enter
   `http://192.168.1.12:5000` → "Connected to HotelPOS Restaurant".
2. Choose Waiter, sign in `waiter1`; Devices shows `APP-WAITER-xxxxxx`.
3. Occupy, order with options and a note, send → kitchen tablet (app or browser) shows the ticket; accept/start/ready.
4. Kill the app (swipe away), reopen → still signed in (SecureStorage refresh), tables load.
5. Airplane mode send → draft kept; reconnect → one ticket.
6. Deactivate the device in WPF → app returns to sign-in with the reason.
7. Install a newer APK over the old one → settings and sign-in survive.

## 12. Acceptance criteria

- [ ] One set of Razor pages serves browser and Android (no duplicated screens).
- [ ] Browser client behaves exactly as Phase 18 (tests and `/app` hosting unchanged).
- [ ] Signed APK installs on Android 8+; first run asks for the server; sign-in works over HTTP on the LAN.
- [ ] Live updates (SignalR) and idempotent sends work in the app; drafts survive an app restart.
- [ ] Refresh token never written outside SecureStorage; deactivation and forced re-login behave as on WPF.
- [ ] No API, DTO, schema or business-rule change; all suites green.
- [ ] Definition of Done; § 11 run on a real phone and tablet.

## 13. Risks, notes and owner decisions (►)

- ► **A. Package name and signing key.** Proposed `com.hotelpos.staff`, app name "HotelPOS Staff"; keystore at
  `C:\Users\Arunkumar\.hotelpos-vendor\android-release.keystore` next to the licence private key — **back both up**;
  a lost keystore means existing installs cannot be updated in place.
- ► **B. Toolchain install (~3–4 GB, once).** `maui-android` workload, OpenJDK 17, Android SDK 34. Needs internet
  and about 20 minutes; nothing else on the PC changes.
- **Cookie vs token.** The browser keeps the Phase 18 cookie flow; the app uses the desktop-style token flow with
  SecureStorage. Both are existing server endpoints.
- **WebView origin.** Inside `BlazorWebView` the page origin is `https://0.0.0.0`, so relative URLs to the server
  (images, hub) must be absolute — handled once in `IClientHost`.
- **Updates.** The APK does not self-update; the ops doc describes "download from `/updates` and install over".
  An in-app "new version" notice can read a small `android.json` next to the APK later.
- **Tablet kitchen.** The same APK; landscape is allowed and the screen stays on while the Kitchen Display is open.

## 14. Changes during implementation

_(filled per step)_
