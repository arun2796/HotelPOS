# Recording the live set-up chapter (server, terminals, phones, Android)

> Automated alternative (2026-10-26): `docs/ui-ux/tools/sandbox/run-sandbox.ps1` records this chapter by itself in Windows Sandbox
> (feature `Containers-DisposableClientVM`); the manual run below stays for a real PC or VM and for the phone steps.

The role chapters of the walkthrough video are recorded by the harnesses on their own. The set-up chapter cannot be:
the installers need the UAC prompt (secure desktop, not scriptable) and they change the machine they run on. So this
chapter is recorded **by hand once, on a clean Windows PC or VM**, with the recorder below; the video builder then cuts
and narrates it exactly like the other chapters. Doing it on the clean VM also closes the open install test in
`docs/ops/10-clean-vm-install-test.md`.

Do **not** record it on the development PC: PostgreSQL 18 is already installed there (the EDB installer would only
offer an upgrade), and the server installer would register the HotelPOS API service on port 5000 next to the dev API.

## What you need

- A clean Windows 10/11 x64 PC or VM (8 GB RAM) with the display at 1920x1080 or 1536x864. If it is a VM, record
  **inside** the VM (install ffmpeg there: `winget install Gyan.FFmpeg`) so the video has no VM window chrome.
- Copied into the VM: `artifacts/installers/HotelPOS-Server-Setup-0.1.1.exe`, `HotelPOS-Client-Setup-0.1.1.exe`,
  `artifacts/android/0.1.1/HotelPOS-Staff-0.1.1-debugkey.apk`, and `docs/ui-ux/tools/record-screen.ps1`.
- A phone on the same Wi-Fi for the browser and Android steps (optional: those two steps can be recorded later on the
  dev PC with the phone mirrored, or left out).

## Run

```powershell
# in the VM, from the folder that holds record-screen.ps1
.\record-screen.ps1 -Out C:\rec -Name 00-setup
```

The recorder captures the whole screen. Each time you **start** a step press **ENTER**, type the step id below and press
ENTER again. Type `q` + ENTER when done. Talk is not recorded; the narration is generated from `narration.en.json`.
Take your time inside a step (the builder shows the last part of each step while the voice speaks), but move the mouse
slowly and keep dialogs centred.

| # | Step id (type exactly) | Show on screen | Voice (already written) |
|---|---|---|---|
| 1 | `server-setup` | Right-click `HotelPOS-Server-Setup-0.1.1.exe` → Run as administrator → accept UAC. **PostgreSQL** page: keep "Install the bundled PostgreSQL" (Phase 26: no separate PostgreSQL installer any more). **Database** page: localhost, 5432, hotelpos / hotelpos, choose the hotelpos password, API port 5000. **Create the database** page: the postgres superuser password. | Run HotelPOS Server Setup as administrator… |
| 2 | `admin-and-https` | **Administrator** page: type the first admin password. **HTTPS certificate** page: leave both fields empty. | The Administrator page asks for the first admin password… |
| 3 | `finish-and-check` | Install → Finish (the installer registers the service, firewall, starts and waits for healthy). Open Edge: `http://localhost:5000/health` → `"status":"Healthy"`. Optionally `services.msc` scrolled to **HotelPOS API** Running. | Finish. The installer registers the HotelPOS API service… |
| 4 | `install-terminals` | Run `HotelPOS-Client-Setup-0.1.1.exe` as administrator (on the same VM is fine). Finish, start HotelPOS: first-run dialog → server address `http://localhost:5000`, terminal name `BILLING-01`, type Billing. Sign-in screen appears. | Now the terminals… |
| 5 | `first-sign-in` | Sign in as `admin` with the installer's password → the forced password change → set a new one → Restaurant settings: name, address, GSTIN, invoice prefix, business-day start → Save. Backup page → **Backup now** → OK. | First sign-in… |
| 6 | `phones-browser` *(optional)* | Phone mirrored (e.g. Phone Link or scrcpy): install `hotelpos-root-ca.cer`, open `https://<server>:5001/app`, Add to Home screen, sign-in screen with Waiter / Kitchen. | Phones and tablets need no installer… |
| 7 | `android-app` *(optional)* | Phone mirrored: open `http://<server>:5000/updates/HotelPOS-Staff.apk`, allow install, open HotelPOS Staff, server address, Test connection → Connected, Save, sign in as Waiter. Admin PC: Devices shows APP-WAITER-…. | Or use the Android app, HotelPOS Staff… |

Steps you skip simply do not appear in the video. The talking-only steps of the slide version (`what-goes-where`,
`server-pc`, `roles`) are not needed in a live recording.

## Build

Copy `C:\rec\00-setup.mp4` and `00-setup.json` into `<repo>\artifacts\demo-video-en\record\` on the dev PC, then from
the Playwright scratch folder:

```powershell
$env:LANG_ID = 'en'; $env:OUT_DIR = '<repo>\artifacts\demo-video-en'
$env:EDGE_TTS_PY = '<repo>\artifacts\demo-video-i18n\tts-venv\Scripts\python.exe'
node <repo>\docs\ui-ux\tools\demo-video-i18n.mjs
```

The builder finds `00-setup.json`, puts the chapter first, switches the opening narration to the version that mentions
set-up, and rewrites `HotelPOS-full-walkthrough-en.mp4` and `narration-script.md`. If the recording's screen size is not
16:9 the clip is letterboxed inside the frame automatically.

To change a sentence: edit `setup.steps[].speech` in `docs/ui-ux/tools/i18n/narration.en.json`, delete the matching
`work\00-setup--<id>.mp3`, `.png` and `live-00-setup--<id>.mp4`, and run the builder again.
