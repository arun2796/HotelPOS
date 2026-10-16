# 05 — Troubleshooting

Start with two checks: `http://<server>:5000/health` from the terminal's browser, and the server log
(**Administration → Server**, or `C:\ProgramData\HotelPOS\Api\logs\api-<date>.log` on the server).

| Symptom | Likely cause | What to do |
|---|---|---|
| Every terminal shows 🔴 Disconnected | Server off, service stopped, or network down | On the server: `Get-Service HotelPOSApi`; start it. If it stops again, read the log. Check the server's cable/IP. |
| One terminal 🔴, others fine | That terminal's Wi-Fi, or a wrong server address | Check Wi-Fi; Administration → This Terminal shows the address; `Test-NetConnection <server> -Port 5000`. |
| Service starts and stops; log says "cannot start: Cannot connect to the database" | PostgreSQL not running, or wrong password in settings | `Get-Service postgresql*`; start it. After a password change run `configure-server.ps1 -DbPassword`. The service retries by itself after a power cut. |
| Log says "needs N migration(s) but Database:AutoMigrate is false" | AutoMigrate turned off by hand | Take a backup, set `"AutoMigrate": true` in `appsettings.Production.json`, restart. |
| `/health` says **Degraded**, `problems.disk` | Less than 2 GB free on a drive with logs, pictures or backups | Free space; move the backup folder to a larger drive (Backup screen); reduce "Keep for". |
| Backup screen shows a red failure banner | `pg_dump` not found, folder not writable, disk full, database unreachable | The banner and the log name the reason. `pg_dump` not found: set `"Backup": { "PgBinPath": "C:\\Program Files\\PostgreSQL\\16\\bin" }` in `appsettings.Production.json`. |
| "Update required" on a terminal | The server was upgraded | Install the matching client ([04](04-upgrade.md)). |
| Sign-in says the account is locked | 5 wrong passwords (also wrong manager passwords in approvals) | Wait 15 minutes, or an admin unlocks it in Users. |
| "Too many attempts. Try again later." | Rate limit on sign-in / approvals | Wait one minute. |
| A terminal is signed out with "This terminal has been disabled" | An admin deactivated the device | Administration → Devices → Activate, if intended. |
| Waiters cannot see a screen they need | Role permissions changed | Administration → Roles & Permissions; changes reach signed-in users within the hour (next session renewal) or at once with "force re-login". |
| Prints do not come out | Printer off/out of paper, or wrong printer on that terminal | Status bar shows print failures with Retry; Administration → Printers on that terminal. |
| Browser says "Your connection is not private" / certificate error on `https://<server>:5001` | The root CA is not installed on that device, the address used is not in the certificate (name or IP changed), or the certificate expired (2 years) | Install `hotelpos-root-ca.cer` ([02 § 5](02-lan-and-firewall.md)). If the server's name or IP changed, or the certificate expired, run `new-lan-certificate.ps1` again with the current names and `configure-server.ps1 -CertificatePath <hotelpos.pfx> -CertificatePassword <password>`, then restart the service. |
| Service will not start after installing; log mentions the certificate | The `.pfx` path or password in `appsettings.Production.json` is wrong or the file was moved | Re-run `configure-server.ps1 -CertificatePath … -CertificatePassword …` (or without them to create a new LAN certificate). |
| Times on bills are wrong | Server clock or time zone | Set the server's time zone and enable internet time; restart the service. |

## Collecting information for support

1. **Administration → Server**: errors only, copy the newest entries.
2. Terminal log: `%LocalAppData%\HotelPOS\logs\desktop-<date>.log` on the affected terminal.
3. The version: bottom-right of the app, and `C:\Program Files\HotelPOS\Server\version.txt`.
4. The correlation id shown in an error toast links the terminal's error to the server log entry.
