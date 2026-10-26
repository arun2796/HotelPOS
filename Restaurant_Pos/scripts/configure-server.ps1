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
    [switch] $ConfigOnly,
    # Phase 26: PostgreSQL shipped inside the server package (<InstallDir>\pgsql). With -BundledPostgres the script
    # creates the database cluster, registers its Windows service and generates the superuser password itself.
    [switch] $BundledPostgres,
    [string] $PgBinDir,
    [string] $PgDataDir,
    [string] $PgServiceName = 'HotelPOSDb'
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

if ($SettingsFile -and "$($handover.BundledPostgres)" -eq '1') { $BundledPostgres = $true }

$exe = Join-Path $InstallDir 'HotelPOS.Api.exe'
$template = Join-Path $InstallDir 'appsettings.Production.template.json'
$target = Join-Path $InstallDir 'appsettings.Production.json'
if (-not (Test-Path $exe)) { throw "HotelPOS.Api.exe not found in $InstallDir." }

# ---------------------------------------------------------------- bundled PostgreSQL (Phase 26)
if ($BundledPostgres) {
    if (-not $PgBinDir) { $PgBinDir = Join-Path $InstallDir 'pgsql\bin' }
    if (-not $PgDataDir) { $PgDataDir = Join-Path $DataRoot 'pg\data' }
    if (-not (Test-Path (Join-Path $PgBinDir 'initdb.exe'))) { throw "The bundled PostgreSQL was not found in $PgBinDir." }
    $DbHost = 'localhost'
    $pgRoot = Split-Path $PgDataDir -Parent
    $passwordFile = Join-Path $pgRoot 'postgres-password.txt'
    New-Item -ItemType Directory -Force $pgRoot | Out-Null

    if (-not (Test-Path (Join-Path $PgDataDir 'PG_VERSION'))) {
        # A new cluster. PostgreSQL refuses to run with administrator rights, so initdb runs as the service account
        # (NetworkService) through a one-off scheduled task; the data folder is handed to that account first.
        if (-not $SuperuserPassword) {
            $pwBytes = New-Object byte[] 18
            [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($pwBytes)
            $SuperuserPassword = ([Convert]::ToBase64String($pwBytes) -replace '[^A-Za-z0-9]', 'x')
        }
        # Kept for the technician (restore, psql): readable by Administrators and SYSTEM only, like the settings file.
        Set-Content -Path $passwordFile -Value $SuperuserPassword -Encoding ASCII -NoNewline
        & icacls $pgRoot /inheritance:r /grant:r 'SYSTEM:(OI)(CI)F' 'Administrators:(OI)(CI)F' 'NT AUTHORITY\NETWORK SERVICE:(OI)(CI)M' | Out-Null
        New-Item -ItemType Directory -Force $PgDataDir | Out-Null
        $initLog = Join-Path $pgRoot 'initdb.log'
        $initPwFile = Join-Path $pgRoot 'initdb-password.tmp'
        Copy-Item $passwordFile $initPwFile -Force
        # schtasks limits the task command to 261 characters, so the initdb command line lives in a small batch file.
        $initCmdFile = Join-Path $pgRoot 'initdb.cmd'
        Set-Content -Path $initCmdFile -Encoding ASCII -Value "@echo off`r`n`"$PgBinDir\initdb.exe`" -D `"$PgDataDir`" -U postgres -A scram-sha-256 --pwfile=`"$initPwFile`" -E UTF8 --locale=C > `"$initLog`" 2>&1`r`n"
        $scheduler = Get-Service -Name Schedule -ErrorAction SilentlyContinue
        if ($scheduler -and $scheduler.Status -ne 'Running') { Start-Service -Name Schedule }
        # The task is only ever started by hand (/Run); a start time ahead of now keeps schtasks from warning (its warning
        # about a past /ST comes with exit code 1 although the task is created).
        $startAt = (Get-Date).AddMinutes(3).ToString('HH:mm')
        $created = & schtasks /Create /F /TN 'HotelPOS-initdb' /SC ONCE /ST $startAt /RU 'NT AUTHORITY\NETWORKSERVICE' /TR "`"$initCmdFile`"" 2>&1
        $createdText = ($created | ForEach-Object { "$_" }) -join ' '
        if ($LASTEXITCODE -ne 0 -and $createdText -notmatch 'SUCCESS') { throw "Could not create the one-off task that initialises the database cluster: $createdText" }
        $started = & schtasks /Run /TN 'HotelPOS-initdb' 2>&1
        if ($LASTEXITCODE -ne 0) { throw "Could not start the initdb task: $(($started | ForEach-Object { "$_" }) -join ' ')" }
        $deadline = (Get-Date).AddMinutes(5)
        do {
            Start-Sleep -Seconds 2
            $state = (& schtasks /Query /TN 'HotelPOS-initdb' /FO LIST | Select-String 'Status:' | Select-Object -First 1).ToString()
        } while ($state -match 'Running' -and (Get-Date) -lt $deadline)
        $taskInfo = (& schtasks /Query /TN 'HotelPOS-initdb' /V /FO LIST 2>&1 | Select-String 'Last Run Result|Status:' | ForEach-Object { $_.Line.Trim() }) -join '; '
        & schtasks /Delete /F /TN 'HotelPOS-initdb' | Out-Null
        Remove-Item $initPwFile, $initCmdFile -Force -ErrorAction SilentlyContinue
        if (-not (Test-Path (Join-Path $PgDataDir 'PG_VERSION'))) {
            $said = if ((Test-Path $initLog) -and (Get-Item $initLog).Length -gt 0) { (Get-Content $initLog | Select-Object -Last 3) -join ' ' } else { 'initdb wrote nothing (a missing Visual C++ runtime makes it exit before it prints anything)' }
            throw "initdb did not create the database cluster. $said. Task: $taskInfo"
        }
        Add-Content -Path (Join-Path $PgDataDir 'postgresql.conf') -Encoding ASCII -Value @"

# HotelPOS (written by configure-server.ps1): local connections only, the API port is opened instead.
listen_addresses = 'localhost'
port = $DbPort
logging_collector = on
log_directory = 'log'
"@
        Write-Host "PostgreSQL cluster created in $PgDataDir."
    } elseif (-not $SuperuserPassword -and (Test-Path $passwordFile)) {
        $SuperuserPassword = (Get-Content $passwordFile -Raw).Trim()
    }

    if (-not (Get-Service -Name $PgServiceName -ErrorAction SilentlyContinue)) {
        & (Join-Path $PgBinDir 'pg_ctl.exe') register -N $PgServiceName -D $PgDataDir -S auto -w | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "pg_ctl could not register the Windows service $PgServiceName." }
        & sc.exe description $PgServiceName 'PostgreSQL database for HotelPOS (bundled with the server).' | Out-Null
        & sc.exe failure $PgServiceName reset= 86400 actions= restart/5000/restart/15000/restart/60000 | Out-Null
    }
    if ((Get-Service -Name $PgServiceName).Status -ne 'Running') { Start-Service -Name $PgServiceName }
    $ready = $false
    for ($i = 0; $i -lt 30 -and -not $ready; $i++) {
        & (Join-Path $PgBinDir 'pg_isready.exe') -h localhost -p $DbPort -q 2>$null
        $ready = ($LASTEXITCODE -eq 0)
        if (-not $ready) { Start-Sleep -Seconds 2 }
    }
    if (-not $ready) { throw "The bundled PostgreSQL service $PgServiceName did not start (see $PgDataDir\log)." }
    Write-Host "PostgreSQL service '$PgServiceName' running on port $DbPort."
}

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

# Try the HotelPOS role's own credentials now (when psql is available) instead of letting the service discover a
# missing role or wrong password after a two-minute wait.
if ($DbPassword) {
    $psqlExe = Join-Path $InstallDir 'pgsql\bin\psql.exe'
    if (-not (Test-Path $psqlExe)) { $psqlExe = (Get-Command psql.exe -ErrorAction SilentlyContinue).Source }
    if (-not $psqlExe) {
        $psqlExe = Get-ChildItem "$env:ProgramFiles\PostgreSQL" -Directory -ErrorAction SilentlyContinue |
            Sort-Object { [int]($_.Name -replace '\D', '0') } -Descending |
            ForEach-Object { Join-Path $_.FullName 'bin\psql.exe' } | Where-Object { Test-Path $_ } | Select-Object -First 1
    }
    if ($psqlExe) {
        $env:PGPASSWORD = $DbPassword
        $previous = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
        $check = & $psqlExe -h $DbHost -p $DbPort -U $DbUser -d postgres -At -c 'select 1' 2>&1
        $ErrorActionPreference = $previous
        Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue
        if ($LASTEXITCODE -ne 0) {
            $said = (($check | ForEach-Object { "$_" }) -join ' ').Trim()
            throw "PostgreSQL refuses the HotelPOS role '$DbUser' with this password ($said). On a new installation enter the postgres (superuser) password on the Database page, or run scripts\new-database.ps1, so the role and database are created."
        }
        Write-Host "Database role '$DbUser' accepted by PostgreSQL."
    }
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

# Backups run pg_dump from the bundled PostgreSQL when the server carries one (Phase 26).
$bundledBin = Join-Path $InstallDir 'pgsql\bin'
if (Test-Path (Join-Path $bundledBin 'pg_dump.exe')) {
    if (-not $settings.PSObject.Properties['Backup']) { $settings | Add-Member -NotePropertyName Backup -NotePropertyValue ([pscustomobject]@{}) }
    if ($settings.Backup.PSObject.Properties['PgBinPath']) { $settings.Backup.PgBinPath = $bundledBin }
    else { $settings.Backup | Add-Member -NotePropertyName PgBinPath -NotePropertyValue $bundledBin }
}

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
