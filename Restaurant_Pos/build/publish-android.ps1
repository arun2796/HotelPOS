<#
.SYNOPSIS
  Builds the signed release APK of HotelPOS Staff (Phase 19) and places it where staff can install it.

.DESCRIPTION
  1. Reads the version from build\version.props (same as the server and desktop).
  2. Creates the release keystore once if it does not exist (prompts for a password) — keep it with the licence key
     and back it up: a lost keystore means existing installs cannot be updated in place.
  3. dotnet publish src\HotelPOS.Android for net8.0-android, Release, signed.
  4. Copies the APK to artifacts\android\<version>\HotelPOS-Staff-<version>.apk and, when -UpdatesFolder exists,
     to the server's /updates folder so phones can download it from http(s)://<server>/updates/HotelPOS-Staff.apk.

.EXAMPLE
  .\build\publish-android.ps1
  .\build\publish-android.ps1 -UpdatesFolder 'C:\ProgramData\HotelPOS\Api\updates'
#>
[CmdletBinding()]
param(
    [string] $KeyStore = (Join-Path $env:USERPROFILE '.hotelpos-vendor\android-release.keystore'),
    [string] $KeyAlias = 'hotelpos',
    [securestring] $KeyStorePassword,
    [string] $UpdatesFolder = '',
    [string] $AndroidSdkDirectory = $env:ANDROID_HOME,
    [string] $JavaSdkDirectory = $env:JAVA_HOME
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
# The release version lives in build/version.props (Directory.Build.props only imports it), same as publish.ps1.
$props = [xml](Get-Content (Join-Path $PSScriptRoot 'version.props'))
$version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { throw 'No <Version> in build/version.props.' }
$versionCode = [int]([version]$version).Major * 10000 + [int]([version]$version).Minor * 100 + [int]([version]$version).Build

if (-not $KeyStorePassword) { $KeyStorePassword = Read-Host -AsSecureString 'Keystore password' }
$plain = [Runtime.InteropServices.Marshal]::PtrToStringUni([Runtime.InteropServices.Marshal]::SecureStringToGlobalAllocUnicode($KeyStorePassword))

if (-not (Test-Path $KeyStore)) {
    Write-Host "Creating release keystore $KeyStore (once)..."
    New-Item -ItemType Directory -Force (Split-Path $KeyStore) | Out-Null
    $keytool = if ($JavaSdkDirectory) { Join-Path $JavaSdkDirectory 'bin\keytool.exe' } else { 'keytool' }
    & $keytool -genkeypair -v -keystore $KeyStore -alias $KeyAlias -keyalg RSA -keysize 2048 -validity 10000 `
        -storepass $plain -keypass $plain -dname 'CN=HotelPOS Staff, O=HotelPOS, C=IN'
    if ($LASTEXITCODE -ne 0) { throw 'keytool failed.' }
}

$out = Join-Path $root "artifacts\android\$version"
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force $out | Out-Null

$args = @(
    (Join-Path $root 'src\HotelPOS.Android'), '-f', 'net8.0-android', '-c', 'Release',
    "-p:ApplicationDisplayVersion=$version", "-p:ApplicationVersion=$versionCode",
    '-p:AndroidKeyStore=true', "-p:AndroidSigningKeyStore=$KeyStore", "-p:AndroidSigningKeyAlias=$KeyAlias",
    "-p:AndroidSigningKeyPass=$plain", "-p:AndroidSigningStorePass=$plain",
    '-p:AndroidPackageFormat=apk', '-o', $out
)
if ($AndroidSdkDirectory) { $args += "-p:AndroidSdkDirectory=$AndroidSdkDirectory" }
if ($JavaSdkDirectory) { $args += "-p:JavaSdkDirectory=$JavaSdkDirectory" }
dotnet publish @args
if ($LASTEXITCODE -ne 0) { throw 'Publishing the Android app failed.' }

$apk = Get-ChildItem $out -Filter '*-Signed.apk' | Select-Object -First 1
if (-not $apk) { throw "No signed APK in $out." }
$final = Join-Path $out "HotelPOS-Staff-$version.apk"
Move-Item $apk.FullName $final -Force
Get-ChildItem $out -Exclude (Split-Path $final -Leaf) | Remove-Item -Recurse -Force
Write-Host "APK: $final"

if ($UpdatesFolder -and (Test-Path $UpdatesFolder)) {
    Copy-Item $final (Join-Path $UpdatesFolder 'HotelPOS-Staff.apk') -Force
    @{ version = $version; file = 'HotelPOS-Staff.apk' } | ConvertTo-Json | Set-Content (Join-Path $UpdatesFolder 'android.json') -Encoding utf8
    Write-Host "Copied to $UpdatesFolder (download at /updates/HotelPOS-Staff.apk)."
}
