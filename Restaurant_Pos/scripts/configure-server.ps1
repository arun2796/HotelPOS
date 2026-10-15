<#
.SYNOPSIS
    Configures an installed HotelPOS server: production settings, data folders, Windows Service, firewall, start.

.DESCRIPTION
    Run by the server installer (elevated) and safe to run again by hand, e.g. after changing the database
    password. On a re-run the JWT signing key is kept (users stay signed in) and values not given are kept.

    Secrets: the settings file is readable by Administrators and SYSTEM (the service account) only. The installer
    hands over passwords in a temporary JSON file (-SettingsFile), which this script deletes after reading.

.EXAMPLE
    .\configure-server.ps1 -DbPassword 'S3cret!' -AdminPassword 'Admin@2026'
.EXAMPLE
    # Settings only, no service or firewall (for a trial run of the published folder).
    .\configure-server.ps1 -SettingsFile .\install.json -ConfigOnly
#>
[CmdletBinding()]
param(
    [string] $InstallDir = (Split-Path -Parent $PSScriptRoot),
    [string] $SettingsFile,
    [string] $DbHost = 'localhost',
    [int] $DbPort = 5432,
    [string] $Database = 'hotelpos',
    [string] $DbUser = 'hotelpos',
    [string] $DbPassword,
    [string] $AdminPassword,
    [int] $Port = 5000,
    [string] $DataRoot = 'C:\ProgramData\HotelPOS',
    [string] $ServiceName = 'HotelPOSApi',
    [switch] $ConfigOnly
)

$ErrorActionPreference = 'Stop'

if ($SettingsFile) {
    $handover = Get-Content $SettingsFile -Raw | ConvertFrom-Json
    Remove-Item $SettingsFile -Force
    foreach ($name in 'DbHost', 'DbPort', 'Database', 'DbUser', 'DbPassword', 'AdminPassword', 'Port', 'DataRoot') {
        if ($null -ne $handover.$name -and "$($handover.$name)" -ne '') { Set-Variable -Name $name -Value $handover.$name }
    }
}

$exe = Join-Path $InstallDir 'HotelPOS.Api.exe'
$template = Join-Path $InstallDir 'appsettings.Production.template.json'
$target = Join-Path $InstallDir 'appsettings.Production.json'
if (-not (Test-Path $exe)) { throw "HotelPOS.Api.exe not found in $InstallDir." }

# Fail with a useful message now rather than a service that keeps restarting.
$probe = New-Object Net.Sockets.TcpClient
try {
    if (-not $probe.ConnectAsync($DbHost, $DbPort).Wait(5000)) { throw 'timeout' }
} catch {
    throw "PostgreSQL is not reachable at ${DbHost}:$DbPort. Install or start PostgreSQL first."
} finally {
    $probe.Dispose()
}

$settings = Get-Content $template -Raw | ConvertFrom-Json
$existing = if (Test-Path $target) { Get-Content $target -Raw | ConvertFrom-Json } else { $null }

function Quote-ConnectionValue([string] $value) {
    # Npgsql accepts quoted values; quoting keeps ';' and '=' inside a password from splitting the string.
    if ($value -match "[;='`"\s]") { return "'" + $value.Replace("'", "''") + "'" }
    return $value
}

if ($DbPassword) {
    $settings.ConnectionStrings.HotelPOS = "Host=$DbHost;Port=$DbPort;Database=$Database;Username=$DbUser;Password=$(Quote-ConnectionValue $DbPassword)"
} elseif ($existing) {
    $settings.ConnectionStrings.HotelPOS = $existing.ConnectionStrings.HotelPOS
} else {
    throw 'A database password is needed on the first configuration (-DbPassword).'
}

if ($existing -and $existing.Jwt.SigningKey -and $existing.Jwt.SigningKey -notlike '<*') {
    $settings.Jwt.SigningKey = $existing.Jwt.SigningKey
} else {
    $bytes = New-Object byte[] 48
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    $settings.Jwt.SigningKey = [Convert]::ToBase64String($bytes)
}

# Only used when the admin account is first created; afterwards the password is changed in the app.
$settings.Seed.AdminPassword = if ($AdminPassword) { $AdminPassword } elseif ($existing) { $existing.Seed.AdminPassword } else { 'Admin@123' }
$settings.Urls = "http://0.0.0.0:$Port"
$settings.Logging.Directory = Join-Path $DataRoot 'Api\logs'
$settings.Media.RootPath = Join-Path $DataRoot 'Api\media'

foreach ($folder in $settings.Logging.Directory, $settings.Media.RootPath, (Join-Path $DataRoot 'Backups')) {
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
}

$settings | ConvertTo-Json -Depth 10 | Set-Content -Path $target -Encoding UTF8
# Administrators and SYSTEM only: the file holds the database password and the token signing key.
& icacls $target /inheritance:r /grant:r '*S-1-5-32-544:F' '*S-1-5-18:F' | Out-Null
Write-Host "Settings written to $target"

if ($ConfigOnly) { return }

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -ne 'Stopped') { Stop-Service -Name $ServiceName -Force }
    & sc.exe config $ServiceName binPath= "`"$exe`"" start= auto | Out-Null
} else {
    New-Service -Name $ServiceName -DisplayName 'HotelPOS API' -BinaryPathName "`"$exe`"" -StartupType Automatic `
        -Description 'HotelPOS restaurant server (REST API, real-time hub, scheduled backups).' | Out-Null
}

# Restart after a crash or a failed start-up check (e.g. PostgreSQL not up yet after a power cut).
& sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/15000/restart/60000 | Out-Null
& (Join-Path $PSScriptRoot 'create-firewall-rule.ps1') -Port $Port

Start-Service -Name $ServiceName
Write-Host "Service '$ServiceName' started; waiting for http://localhost:$Port/health ..."
for ($i = 0; $i -lt 60; $i++) {
    try {
        $health = Invoke-RestMethod "http://localhost:$Port/health" -TimeoutSec 5
        Write-Host "Server is up: $($health.data.status)"
        return
    } catch {
        Start-Sleep -Seconds 2
    }
}

throw "The service did not answer /health within 2 minutes. See $($settings.Logging.Directory)."
