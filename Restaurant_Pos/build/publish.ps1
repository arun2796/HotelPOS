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
    .NET SDK. The script refuses to produce a release whose ASP.NET Core runtime is older than 8.0.32
    (CVE-2026-70159, SignalR denial of service): update the .NET 8 SDK on the build PC first.

.EXAMPLE
    .\build\publish.ps1
.EXAMPLE
    # A local trial build on a PC with an older SDK; never give this output to a venue.
    .\build\publish.ps1 -SkipTests -AllowOutdatedRuntime
#>
param(
    [switch] $SkipTests,
    [switch] $AllowOutdatedRuntime,
    [string] $InnoCompiler = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$minimumAspNetCore = [Version]'8.0.32'

[xml] $props = Get-Content (Join-Path $PSScriptRoot 'version.props')
$version = $props.Project.PropertyGroup.Version
$out = Join-Path $root "artifacts\publish\$version"
Write-Host "HotelPOS $version -> $out"

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
    $message = "The bundled ASP.NET Core runtime $shipped is older than $minimumAspNetCore (CVE-2026-70159). Install the latest .NET 8 SDK and publish again."
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
    foreach ($script in 'server.iss', 'client.iss') {
        & $InnoCompiler "/DAppVersion=$version" "/DPublishDir=$out" "/O$(Join-Path $root 'artifacts\installers')" (Join-Path $root "installer\$script")
        if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed on $script." }
    }
} else {
    Write-Warning "Inno Setup not found at $InnoCompiler; the publish folders are ready but no installers were built."
}

Write-Host 'Done.'
