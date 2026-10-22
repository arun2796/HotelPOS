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
    # Optional: with the postgres superuser password the role and database are created when missing (new-database.ps1).
    [string] $SuperuserPassword,
    [string] $AdminPassword,
    [int] $Port = 5000,
    [int] $HttpsPort = 5001,
    # Optional .pfx for the HTTPS endpoint; without one a LAN root CA + server certificate is created (new-lan-certificate.ps1).
    [string] $CertificatePath,
    [string] $CertificatePassword,
    [string] $DataRoot = 'C:\ProgramData\HotelPOS',
    [string] $ServiceName = 'HotelPOSApi',
    [switch] $ConfigOnly
)

$ErrorActionPreference = 'Stop'

# One clean "ERROR: <reason>" line as the last output when the script stops: the installer shows it in its dialog
# (PowerShell's own error dump is wrapped and ends with stack lines) and configure-server.log keeps it.
trap {
    Write-Host "ERROR: $($_.Exception.Message)"
    exit 1
}

if ($SettingsFile) {
    $handover = Get-Content $SettingsFile -Raw | ConvertFrom-Json
    Remove-Item $SettingsFile -Force
    foreach ($name in 'DbHost', 'DbPort', 'Database', 'DbUser', 'DbPassword', 'SuperuserPassword', 'AdminPassword', 'Port', 'HttpsPort', 'CertificatePath', 'CertificatePassword', 'DataRoot') {
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

# With the superuser password the role and database are made here, so a new installation needs no psql session.
if ($SuperuserPassword) {
    if (-not $DbPassword) { throw 'A password for the HotelPOS database role is needed together with the postgres password (-DbPassword).' }
    & (Join-Path $PSScriptRoot 'new-database.ps1') -DbHost $DbHost -DbPort $DbPort -Database $Database -DbUser $DbUser `
        -DbPassword $DbPassword -SuperuserPassword $SuperuserPassword
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

# Only used when the admin account is first created; afterwards the password is changed in the app. Never a default:
# the server refuses to create the administrator without one (anything on the LAN could sign in with a known password).
$settings.Seed.AdminPassword = if ($AdminPassword) { $AdminPassword } elseif ($existing) { $existing.Seed.AdminPassword } else {
    throw 'The first administrator password is needed on a new installation (-AdminPassword). It must be changed at the first sign-in.'
}
# Plain HTTP stays for the Windows terminals; HTTPS (phones, PWA install) is on 5001. A "Urls" value would override
# the Kestrel endpoints, so it is cleared (the base appsettings.json has one for Development).
$settings.Urls = ''

# Certificate: given (-CertificatePath), else kept from the previous configuration, else a LAN certificate is created.
$certPath = $CertificatePath
$certPassword = $CertificatePassword
$keptCert = if ($existing) { $existing.Kestrel.Endpoints.Https.Certificate } else { $null }
if (-not $certPath -and $keptCert -and $keptCert.Path -and $keptCert.Path -notlike '<*') {
    $certPath = $keptCert.Path
    if (-not $certPassword) { $certPassword = $keptCert.Password }
}
if (-not $certPath) {
    $pfxBytes = New-Object byte[] 24
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($pfxBytes)
    $certPassword = [Convert]::ToBase64String($pfxBytes)
    $names = @([Net.Dns]::GetHostName()) + @(
        Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
            Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' } |
            ForEach-Object { $_.IPAddress })
    $certFolder = Join-Path $DataRoot 'Api'
    & (Join-Path $PSScriptRoot 'new-lan-certificate.ps1') -DnsNames $names `
        -PfxPassword (ConvertTo-SecureString $certPassword -AsPlainText -Force) -OutputFolder $certFolder
    $certPath = Join-Path $certFolder 'hotelpos.pfx'
}
if (-not (Test-Path $certPath)) { throw "Certificate file not found: $certPath" }
if (-not $certPassword) { throw 'A certificate password is needed (-CertificatePassword).' }
$settings.Kestrel.Endpoints.Http.Url = "http://0.0.0.0:$Port"
$settings.Kestrel.Endpoints.Https.Url = "https://0.0.0.0:$HttpsPort"
$settings.Kestrel.Endpoints.Https.Certificate.Path = $certPath
$settings.Kestrel.Endpoints.Https.Certificate.Password = $certPassword
$settings.Logging.Directory = Join-Path $DataRoot 'Api\logs'
$settings.Media.RootPath = Join-Path $DataRoot 'Api\media'

foreach ($folder in $settings.Logging.Directory, $settings.Media.RootPath, (Join-Path $DataRoot 'Backups')) {
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
}

$settings | ConvertTo-Json -Depth 10 | Set-Content -Path $target -Encoding UTF8
# Administrators and SYSTEM only: the file holds the database and certificate passwords and the token signing key.
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
& (Join-Path $PSScriptRoot 'create-firewall-rule.ps1') -Port $Port -Https -HttpsPort $HttpsPort

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

# Quote the server's own reason (the log line after "cannot start"): usually a database role, database or password
# that does not exist yet (docs/ops/01 § 3), so the technician sees it here instead of hunting for the log.
$logDir = $settings.Logging.Directory
$reason = Get-ChildItem $logDir -Filter 'api-*.log' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1 |
    ForEach-Object { Select-String -Path $_.FullName -Pattern 'cannot start' | Select-Object -Last 1 | ForEach-Object { ($_.Line -split 'cannot start: ', 2)[-1] } }
$state = (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue).Status
throw "The service did not answer /health within 2 minutes (service state: $state). $(if ($reason) { "Reason from the log: $reason" } else { "See $logDir." })"
