<#
.SYNOPSIS
  One-time setup of the Android toolchain for the HotelPOS Staff app (Phase 19) on a build PC.

.DESCRIPTION
  Installs, in this order:
    1. the .NET workload "maui-android" (the SDK that builds net8.0-android);
    2. Microsoft OpenJDK 17 (winget), which the Android build tools need;
    3. the Android SDK pieces (platform 34, build-tools, platform-tools) into -AndroidSdkDirectory, using the
       .NET Android SDK's own installer target, with the licences accepted.

  Run from a PowerShell window; the workload step may ask for elevation when the .NET SDK lives under Program Files.
  Re-running is safe: every step skips what is already present.

.EXAMPLE
  .\build\install-android-tools.ps1
#>
[CmdletBinding()]
param(
    [string] $AndroidSdkDirectory = 'C:\Android\android-sdk',
    [string] $JavaSdkDirectory = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

Write-Host '== 1/3 .NET workload maui-android'
$workloads = dotnet workload list 2>$null
if ($workloads -match '^\s*maui-android\s') {
    Write-Host '   already installed'
} else {
    dotnet workload install maui-android
    if ($LASTEXITCODE -ne 0) { throw 'dotnet workload install maui-android failed (try an elevated PowerShell).' }
}

Write-Host '== 2/3 Microsoft OpenJDK 17'
if (-not $JavaSdkDirectory) {
    $jdk = Get-ChildItem 'C:\Program Files\Microsoft' -Directory -Filter 'jdk-17*' -ErrorAction SilentlyContinue | Sort-Object Name -Descending | Select-Object -First 1
    if (-not $jdk) {
        winget install --id Microsoft.OpenJDK.17 --exact --silent --accept-package-agreements --accept-source-agreements
        if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne -1978335189) { throw "winget install Microsoft.OpenJDK.17 failed ($LASTEXITCODE)." }
        $jdk = Get-ChildItem 'C:\Program Files\Microsoft' -Directory -Filter 'jdk-17*' | Sort-Object Name -Descending | Select-Object -First 1
    }
    $JavaSdkDirectory = $jdk.FullName
}
Write-Host "   JDK: $JavaSdkDirectory"

Write-Host '== 3/3 Android SDK'
New-Item -ItemType Directory -Force $AndroidSdkDirectory | Out-Null
$project = Join-Path $root 'src\HotelPOS.Android\HotelPOS.Android.csproj'
if (-not (Test-Path $project)) { throw "The Android project is not there yet ($project); run this script again after step 19.2 creates it, or pass a project that targets net8.0-android." }
dotnet build $project -t:InstallAndroidDependencies -f net8.0-android `
    "-p:AndroidSdkDirectory=$AndroidSdkDirectory" "-p:JavaSdkDirectory=$JavaSdkDirectory" '-p:AcceptAndroidSDKLicenses=True'
if ($LASTEXITCODE -ne 0) { throw 'Android SDK installation failed.' }

[Environment]::SetEnvironmentVariable('ANDROID_HOME', $AndroidSdkDirectory, 'User')
[Environment]::SetEnvironmentVariable('JAVA_HOME', $JavaSdkDirectory, 'User')
Write-Host ''
Write-Host "Done. ANDROID_HOME=$AndroidSdkDirectory  JAVA_HOME=$JavaSdkDirectory (user environment; open a new window)."
Write-Host 'Build the app with: dotnet build src\HotelPOS.Android -f net8.0-android'
