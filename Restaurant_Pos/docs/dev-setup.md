# Developer Setup

Last updated: 2026-10-07 (Phase 2, PostgreSQL)

How to build, run and test HotelPOS on a development PC. Production installation is covered in Phase 10
(`docs/ops/`).

## 1. Prerequisites

| Tool | Version | Notes |
|---|---|---|
| Windows | 10 / 11 | The desktop client is WPF (Windows only). |
| .NET SDK | 8.0.4xx | Pinned by `global.json` (roll-forward to the latest 8.0 feature band). |
| PostgreSQL | 16+ (developed on 18) | Local server on `localhost:5432`, used for development and tests. Windows installer from postgresql.org. |
| IDE | Visual Studio 2022, Rider or VS Code + C# Dev Kit | |

Check:

```powershell
dotnet --list-sdks          # an 8.0.x SDK must be listed
psql -h localhost -U postgres -c "select version();"   # the server must answer
```

Restore the local tools once (EF Core CLI pinned to 8.0.x):

```powershell
dotnet tool restore
```

## 2. Solution layout

```
HotelPOS.sln
src/
  HotelPOS.Contracts        DTOs, enums, error codes, roles, hub event names (shared with the desktop)
  HotelPOS.Domain           entities and domain rules
  HotelPOS.Application      use-case services, validators, interfaces
  HotelPOS.Infrastructure   EF Core (PostgreSQL via Npgsql), migrations, seeding, JWT, password hashing, audit
  HotelPOS.Api              ASP.NET Core Web API + SignalR hub (/hubs/restaurant)
  HotelPOS.Desktop          WPF client (all roles)
tests/
  HotelPOS.Domain.Tests       pure unit tests
  HotelPOS.Application.Tests  services against a throw-away PostgreSQL database
  HotelPOS.Api.Tests          in-memory API (WebApplicationFactory) against a throw-away database
  HotelPOS.Desktop.Tests      view-models, screen rendering, optional end-to-end resilience test
```

Package versions are central in `Directory.Packages.props`. `Directory.Build.props` turns warnings into
errors for everything under `src/`.

## 3. Build and test

```powershell
dotnet build HotelPOS.sln
dotnet test HotelPOS.sln
```

The Application and API tests create databases named `hotelpos_apptests_<guid>` / `hotelpos_apitests_<guid>`
on the local PostgreSQL server and drop them afterwards. Only `hotelpos_*` databases are ever created or
dropped. The default server account is in `tests/Shared/TestPostgres.cs`; to use another server or account
set a connection string without `Database`:

```powershell
$env:HOTELPOS_TEST_POSTGRES = "Host=localhost;Port=5432;Username=postgres;Password=<password>"
```

### Screen rendering test

`ScreenRenderingTests` builds every screen with sample data on a WPF UI thread, fails on XAML or binding
errors, and saves screenshots to `%TEMP%\hotelpos-screens`. Open them to review the UI without clicking
through the app.

### End-to-end resilience test (opt-in)

Starts the built API as a real process, signs in through the real desktop services, kills the server,
restarts it and checks that the client reports the outage, reconnects by itself and keeps its session.

```powershell
dotnet build HotelPOS.sln
$env:HOTELPOS_E2E = "1"
dotnet test tests/HotelPOS.Desktop.Tests --filter "FullyQualifiedName~EndToEnd"
```

It uses (and keeps) a PostgreSQL database named `hotelpos_e2e` on the same server as the other tests.

## 4. Run the API

```powershell
dotnet run --project src/HotelPOS.Api
```

The `Development` profile:

- listens on `http://0.0.0.0:5000` (all interfaces, so other PCs on the LAN can connect — Windows may ask
  to allow the port through the firewall);
- uses the PostgreSQL database `hotelpos_dev` (connection string in `appsettings.Development.json`), created
  and migrated automatically at start-up;
- seeds the admin account, demo users, a demo floor (Ground Floor T01–T08, First Floor T11–T14,
  Outdoor O01–O04) and a demo menu (Starters, Biryani, Breads, Beverages, Desserts; MAIN and BAR stations;
  GST 5%/18%; Spice level, Add-ons and Sugar modifiers);
- stores uploaded menu pictures in `src/HotelPOS.Api/media/menu` (git-ignored; `Media:RootPath` to change)
  and serves them at `/images/menu/...`;
- serves Swagger UI at `http://localhost:5000/swagger`.

To listen on localhost only: `dotnet run --project src/HotelPOS.Api -- --urls http://localhost:5000`.

Useful endpoints:

| URL | Purpose |
|---|---|
| `GET /health` | Liveness + database check (anonymous) |
| `GET /api/system/info` | Restaurant name and API version (anonymous) |
| `POST /api/auth/login` | Sign in |
| `GET /api/menu` | Compact ordering menu (`?version=N` answers `notModified` when unchanged) |
| `/hubs/restaurant` | SignalR hub |

API logs: `src/HotelPOS.Api/logs/api-YYYYMMDD.log` (and the console).

### Development accounts

| Username | Password | Role | Notes |
|---|---|---|---|
| `admin` | `Admin@123` | Admin | Must change the password at first sign-in |
| `manager1` | `Pass@123` | Manager | Demo data (Development only) |
| `waiter1`, `waiter2` | `Pass@123` | Waiter | |
| `kitchen1` | `Pass@123` | Kitchen | |
| `cashier1` | `Pass@123` | Cashier | |

Demo users are created only when `Seed:DemoData` is true (`appsettings.Development.json`). In production
the admin password comes from `Seed:AdminPassword`; if it is empty, `Admin@123` is used and must be changed
at first sign-in (a warning is logged).

### Database commands

```powershell
# Add a migration (one per phase at least: Phase0N_<Topic>)
dotnet ef migrations add Phase02_Tables --project src/HotelPOS.Infrastructure --startup-project src/HotelPOS.Infrastructure --output-dir Persistence/Migrations

# Apply migrations manually (normally done by the API at start-up)
dotnet ef database update --project src/HotelPOS.Infrastructure --startup-project src/HotelPOS.Infrastructure

# Reset the development database (stop the API first); it is recreated at the next start
psql -h localhost -U postgres -c "DROP DATABASE IF EXISTS hotelpos_dev WITH (FORCE);"

# Inspect it
psql -h localhost -U postgres -d hotelpos_dev -c '\dt'
```

`dotnet ef migrations add` needs no database. `dotnet ef database update` uses `DesignTimeDbContextFactory`
(`hotelpos_dev` on localhost); set `HOTELPOS_DESIGN_CONNECTION` to a full connection string (with password)
to target another server.

### PostgreSQL specifics

- Tables and columns keep their PascalCase names, so quote them in hand-written SQL: `SELECT * FROM "Tables";`.
- Optimistic concurrency uses the system column `xmin` (mapped to `RowVersion`); it is not a real column and
  does not appear in `\d "Tables"`.
- Timestamps are `timestamp(3) with time zone` and always written in UTC.
- Text comparisons are case-sensitive; code that needs case-insensitive matching upper-cases both sides
  (see `UserService` search and section names).

## 5. Run the desktop client

```powershell
dotnet run --project src/HotelPOS.Desktop
```

On first start the **Connect this terminal** screen asks for the server address
(`http://localhost:5000` on the same PC, or `http://<server-ip>:5000` from another PC) and the terminal name
and type. **Test connection** checks the address before saving.

### Several terminals on one PC

Use a profile per terminal; each profile has its own settings, session and log file:

```powershell
dotnet run --project src/HotelPOS.Desktop -- --profile waiter2
dotnet run --project src/HotelPOS.Desktop -- --profile kitchen
```

### Where the client keeps its files

| What | Location |
|---|---|
| Terminal settings | `%ProgramData%\HotelPOS\Desktop\settings[.profile].json` (falls back to `%LocalAppData%\HotelPOS\Desktop\` if ProgramData is not writable) |
| Session (refresh token, DPAPI-encrypted) | `%LocalAppData%\HotelPOS\<profile>\session.dat` |
| Preferences (last username) | `%LocalAppData%\HotelPOS\<profile>\preferences.json` |
| Logs | `%LocalAppData%\HotelPOS\logs\desktop[-profile]-YYYYMMDD.log` |
| Menu pictures (cache) | `%LocalAppData%\HotelPOS\cache\images` (safe to delete; downloaded again when needed) |

Delete the settings file to see the first-run screen again. **F11** toggles full screen.

## 6. Troubleshooting

| Symptom | Check |
|---|---|
| API fails at start with "Connection string 'HotelPOS' is not configured" | Run with `ASPNETCORE_ENVIRONMENT=Development` or set `ConnectionStrings:HotelPOS`. |
| API fails with "Jwt:SigningKey must be configured" | Set `Jwt:SigningKey` (≥ 32 characters). Development has one in `appsettings.Development.json`. |
| Desktop says "Cannot reach the server" | API running? Address and port right? On another PC: firewall rule for TCP 5000 on the server. |
| Desktop status bar stays amber/red | The hub reconnects automatically (2 s → 15 s back-off); check the API log for errors. |
| Tests or API fail with "password authentication failed" / "No password has been provided" | Check the password in `appsettings.Development.json` / `tests/Shared/TestPostgres.cs`, or set `HOTELPOS_TEST_POSTGRES`. |
| "Connection refused" on port 5432 | Start the Windows service `postgresql-x64-<version>` (`services.msc`). |
