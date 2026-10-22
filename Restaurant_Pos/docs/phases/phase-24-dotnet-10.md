# Phase 24 — .NET 10 LTS

.NET 8 leaves Microsoft support on 2026-11-10 (no more security patches). Every project moves to .NET 10 LTS
(supported until November 2028) with the matching package line. No behaviour change intended.

## What changed

| Item | Before | After |
|------|--------|-------|
| `global.json` | SDK 8.0.417, roll-forward latestFeature | SDK 10.0.300, roll-forward latestFeature |
| Target frameworks (16 projects) | `net8.0`, `net8.0-windows`, `net8.0-android` | `net10.0`, `net10.0-windows`, `net10.0-android` |
| EF Core / Npgsql provider | 8.0.31 / 8.0.11 | 10.0.13 / 10.0.3 (`dotnet-ef` 10.0.13) |
| ASP.NET Core packages (JwtBearer, SignalR client, Mvc.Testing, WebAssembly.Server) and `Microsoft.Extensions.*` | 8.0.3x | 10.0.13 |
| Blazor WebAssembly client family (`Components.WebAssembly`, `.DevServer`, `Components.Web`, `Components.Authorization`) | 8.0.32 | 10.0.8 — kept on the runtime the build SDK bundles, because the WebAssembly runtime pack comes from the SDK |
| Serilog.AspNetCore / Extensions.Hosting / Sinks.File | 8.0.3 / 8.0.0 / 5.0.0 | 10.0.0 / 10.0.0 / 7.0.0 |
| `System.IdentityModel.Tokens.Jwt` | 8.0.2 | 8.23.0 (what JwtBearer 10 needs) |
| `System.Security.Cryptography.ProtectedData` | package | removed: part of the framework on `net10.0-windows` (NU1510) |
| `AngleSharp` (test only) | transitive 1.2.0 via bunit (NU1902) | direct pin 1.3.0: the highest version bunit 1.40 runs with (1.4+ throws `MissingMethodException`); the moderate advisory GHSA-pgww-w46g-26qg stays in the test project only. Follow-up: bunit 2 |
| Workloads on the build PC | `maui-android`, `wasm-tools` for SDK 8 | the same for SDK 10, plus Android SDK platform 36 (`build/install-android-tools.ps1`) |
| `build/publish.ps1` runtime gate | refuses ASP.NET Core < 8.0.32 | refuses < 10.0.8 |
| Scripts / tests naming the TFM | `net8.0-android`, `bin/<cfg>/net8.0` | `net10.0-android`, `bin/<cfg>/net10.0` |

Unchanged on purpose: MaterialDesignThemes 5.1 (desktop look verified by the rendering tests), SkiaSharp 3.119
(4.x is a major), FluentAssertions 6.12 (7+ changed licence), Swashbuckle 6.9 (Development only), xunit 2.9,
QuestPDF 2026.9, MediatR 12.5 (Apache 2.0; 13+ is commercial).

No source change was needed: the solution compiled on .NET 10 as it was. The EF Core 10 model matches the migration
snapshot (`dotnet ef migrations has-pending-model-changes`), so a venue upgrade applies only the Phase 23 migration.
`dotnet list package --vulnerable --include-transitive` is clean for every shipped project (API, desktop, browser
client); the only advisory left is AngleSharp in the browser-client test project (above).

## Verification

See § Log for the suite numbers on .NET 10. Still to do on real hardware: the clean-VM install test (docs/ops/10)
with installers built by `publish.ps1` on the .NET 10 SDK, and the Android app on the owner's phone (Release APK
built; `publish-android.ps1` signs it with the release keystore).

## Log

- 2026-10-22: branch `phase-24-dotnet-10` from main 825fb6e (Phase 23). SDK, TFMs, packages, workloads, Android
  platform 36, publish gate, docs. Full solution and Release Android build green. Suite on .NET 10: Domain 414,
  Client 233, Api 205, Application 296, Desktop 456 + 2 skipped — the same numbers as on .NET 8.
