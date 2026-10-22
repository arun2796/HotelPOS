<#
.SYNOPSIS
    Builds the release: tests, self-contained win-x64 publish of the API and the desktop client, the scripts, and
    (when Inno Setup is installed) the two installers.

.DESCRIPTION
    Output (artifacts\ is gitignored):
        artifacts\publish\<version>\server   API + scripts + appsettings.Production.template.json
        artifacts\publish\<version>\client   desktop app
        artifacts\installers\                HotelPOS-Server-Setup-<version>.exe, HotelPOS-Client-Setup-<version>.exe

    Self-contained means the venue PCs need no .NET install, but the build carries the runtime of THIS machine's
    .NET SDK. The script refuses to produce a release whose ASP.NET Core runtime is older than $minimumAspNetCore
    (.NET 10 LTS; .NET 8 left support on 2026-11-10, and 8.0.32 had closed CVE-2026-70159): update the .NET 10 SDK
    on the build PC first.

.EXAMPLE
    .\build\publish.ps1
.EXAMPLE
    # A local trial build on a PC with an older SDK; never give this output to a venue.
    .\build\publish.ps1 -SkipTests -AllowOutdatedRuntime
#>
param(
    [switch] $SkipTests,
    [switch] $AllowOutdatedRuntime,
    [string] $InnoCompiler,
    # The vendor's licence private key (PEM) also signs updates\latest.json; terminals refuse an unsigned manifest.
    [string] $VendorKey = (Join-Path $env:USERPROFILE '.hotelpos-vendor\license-private.pem')
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$minimumAspNetCore = [Version]'10.0.8'

# Inno Setup installs per machine (Program Files (x86)) or per user (LocalAppData, the winget default); accept either.
if (-not $InnoCompiler) {
    $candidates = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe")
    $InnoCompiler = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $InnoCompiler) { $InnoCompiler = $candidates[0] }
}

[xml] $props = Get-Content (Join-Path $PSScriptRoot 'version.props')
$version = $props.Project.PropertyGroup.Version
$out = Join-Path $root "artifacts\publish\$version"
Write-Host "HotelPOS $version -> $out"

# Checked before the long build: without the key the client installer could not be signed into latest.json and no
# terminal would accept the self-update.
if ((Test-Path $InnoCompiler) -and -not (Test-Path $VendorKey)) {
    throw "Vendor private key not found at $VendorKey (needed to sign updates\latest.json). Pass -VendorKey <file> or restore the key from its backup (docs/ops/08)."
}

if (-not $SkipTests) {
    dotnet test (Join-Path $root 'HotelPOS.sln') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed; nothing was published.' }
}

if (Test-Path $out) { Remove-Item -Recurse -Force $out }

$common = @('-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:DebugType=None', '-p:DebugSymbols=false')
dotnet publish (Join-Path $root 'src\HotelPOS.Api') @common -o (Join-Path $out 'server')
if ($LASTEXITCODE -ne 0) { throw 'Publishing the API failed.' }
dotnet publish (Join-Path $root 'src\HotelPOS.Desktop') @common -o (Join-Path $out 'client')
if ($LASTEXITCODE -ne 0) { throw 'Publishing the desktop client failed.' }

# The runtime that actually ships, read from the published server rather than assumed from the SDK.
$signalR = Get-Item (Join-Path $out 'server\Microsoft.AspNetCore.SignalR.Core.dll')
$shipped = [Version]($signalR.VersionInfo.ProductVersion -split '[+-]')[0]
Write-Host "Bundled ASP.NET Core runtime: $shipped"
if ($shipped -lt $minimumAspNetCore) {
    $message = "The bundled ASP.NET Core runtime $shipped is older than $minimumAspNetCore (.NET 10 LTS is required). Install the latest .NET 10 SDK and publish again."
    if (-not $AllowOutdatedRuntime) { Remove-Item -Recurse -Force $out; throw $message }
    Write-Warning "$message This output is for local trials only."
    Set-Content (Join-Path $out 'NOT-FOR-RELEASE.txt') $message
}

if (Test-Path (Join-Path $out 'server\appsettings.Development.json')) {
    throw 'appsettings.Development.json must not be published.'
}

Copy-Item -Recurse (Join-Path $root 'scripts') (Join-Path $out 'server\scripts')
Set-Content (Join-Path $out 'version.txt') "HotelPOS $version, ASP.NET Core $shipped, built $(Get-Date -Format s)"

if (Test-Path $InnoCompiler) {
    # The client installer first: the server hosts it under /updates/ (server\updates, which the server installer then
    # carries), and terminals older than latest.json's version download it from the server over the LAN.
    $installers = Join-Path $root 'artifacts\installers'
    & $InnoCompiler "/DAppVersion=$version" "/DPublishDir=$out" "/O$installers" (Join-Path $root 'installer\client.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed on client.iss.' }

    $clientSetup = Join-Path $installers "HotelPOS-Client-Setup-$version.exe"
    $updates = Join-Path $out 'server\updates'
    New-Item -ItemType Directory -Force $updates | Out-Null
    Copy-Item $clientSetup $updates
    [ordered] @{
        version = $version
        file    = Split-Path -Leaf $clientSetup
        sha256  = (Get-FileHash $clientSetup -Algorithm SHA256).Hash
        size    = (Get-Item $clientSetup).Length
    } | ConvertTo-Json | Set-Content (Join-Path $updates 'latest.json') -Encoding utf8

    # The vendor's signature over the manifest: the server advertises, and terminals install, only a signed latest.json.
    dotnet run --project (Join-Path $root 'tools\HotelPOS.LicenseGenerator') -c Release -- sign-update --private-key $VendorKey --manifest (Join-Path $updates 'latest.json')
    if ($LASTEXITCODE -ne 0) { throw 'Signing updates\latest.json failed; the installers were not built.' }

    & $InnoCompiler "/DAppVersion=$version" "/DPublishDir=$out" "/O$installers" (Join-Path $root 'installer\server.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed on server.iss.' }
} else {
    Write-Warning "Inno Setup not found at $InnoCompiler; the publish folders are ready but no installers were built."
}

Write-Host 'Done.'
