<#
.SYNOPSIS
    Restores a HotelPOS backup (pg_dump custom format) into PostgreSQL, and optionally the menu pictures.

.DESCRIPTION
    Steps: verify the file -> stop the HotelPOS API service -> drop and recreate the database -> pg_restore ->
    restore pictures from the matching -media.zip -> start the service. Run it in an elevated PowerShell on the
    server. The PostgreSQL password is read from PGPASSWORD or asked for; it is never written anywhere.
    See docs/ops/03-backup-and-recovery.md.

.EXAMPLE
    .\restore-db.ps1 -BackupFile 'C:\ProgramData\HotelPOS\Backups\hotelpos-20261014-033000.dump'

.EXAMPLE
    # Rehearsal: restore into a scratch database without touching the service or the pictures.
    .\restore-db.ps1 -BackupFile .\hotelpos-20261014-033000.dump -Database hotelpos_restoretest -SkipService -SkipMedia
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory = $true)] [string] $BackupFile,
    [string] $Database = 'hotelpos',
    [string] $DbHost = 'localhost',
    [int] $Port = 5432,
    # A role allowed to drop and create databases (normally postgres).
    [string] $AdminUser = 'postgres',
    # Owner of the restored database: the role the API connects with.
    [string] $Owner = 'hotelpos',
    [string] $PgBin,
    [string] $ServiceName = 'HotelPOSApi',
    [string] $MediaPath = 'C:\ProgramData\HotelPOS\Api\media',
    [switch] $SkipService,
    [switch] $SkipMedia
)

$ErrorActionPreference = 'Stop'

function Find-PgTool([string] $name) {
    if ($PgBin) { $candidate = Join-Path $PgBin "$name.exe"; if (Test-Path $candidate) { return $candidate }; throw "$name.exe not found in $PgBin" }
    $onPath = Get-Command $name -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    $installed = Get-ChildItem 'C:\Program Files\PostgreSQL' -Directory -ErrorAction SilentlyContinue |
        Sort-Object { [int]($_.Name -replace '\D', '0') } -Descending |
        ForEach-Object { Join-Path $_.FullName "bin\$name.exe" } | Where-Object { Test-Path $_ } | Select-Object -First 1
    if ($installed) { return $installed }
    throw "$name was not found. Pass -PgBin with the PostgreSQL bin folder."
}

function Invoke-PgTool([string] $tool, [string[]] $arguments) {
    & $tool @arguments
    if ($LASTEXITCODE -ne 0) { throw "$(Split-Path $tool -Leaf) failed with exit code $LASTEXITCODE." }
}

$BackupFile = (Resolve-Path $BackupFile).Path
$pgRestore = Find-PgTool 'pg_restore'
$dropDb = Find-PgTool 'dropdb'
$createDb = Find-PgTool 'createdb'

if (-not $env:PGPASSWORD) {
    $secure = Read-Host "Password of PostgreSQL user '$AdminUser'" -AsSecureString
    $env:PGPASSWORD = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
}

Write-Host "1/5 Verifying $BackupFile"
$listing = & $pgRestore --list $BackupFile
if ($LASTEXITCODE -ne 0 -or -not ($listing -match 'TABLE DATA')) { throw 'The file is not a readable HotelPOS backup.' }

$service = if ($SkipService) { $null } else { Get-Service -Name $ServiceName -ErrorAction SilentlyContinue }
if ($service -and $service.Status -ne 'Stopped') {
    Write-Host "2/5 Stopping service '$ServiceName'"
    Stop-Service -Name $ServiceName -Force
} else {
    Write-Host '2/5 Service not running or skipped'
}

if (-not $PSCmdlet.ShouldProcess("database '$Database' on ${DbHost}:$Port", 'Drop and restore')) { return }

Write-Host "3/5 Recreating database '$Database' (owner $Owner)"
$connection = @('--host', $DbHost, '--port', $Port, '--username', $AdminUser)
Invoke-PgTool $dropDb ($connection + @('--if-exists', $Database))
Invoke-PgTool $createDb ($connection + @('--owner', $Owner, $Database))

Write-Host '4/5 Restoring data'
Invoke-PgTool $pgRestore ($connection + @('--no-owner', '--role', $Owner, '--dbname', $Database, '--exit-on-error', $BackupFile))

$mediaZip = [IO.Path]::ChangeExtension($BackupFile, $null).TrimEnd('.') + '-media.zip'
if (-not $SkipMedia -and (Test-Path $mediaZip)) {
    $menu = Join-Path $MediaPath 'menu'
    if (Test-Path $menu) {
        $kept = "$menu-before-restore-$(Get-Date -Format yyyyMMddHHmmss)"
        Move-Item $menu $kept
        Write-Host "    Current pictures kept in $kept"
    }
    Expand-Archive -Path $mediaZip -DestinationPath $menu
    Write-Host "5/5 Pictures restored to $menu"
} else {
    Write-Host '5/5 No pictures restored (no -media.zip next to the backup, or skipped)'
}

if ($service) {
    Start-Service -Name $ServiceName
    Write-Host "Service '$ServiceName' started. Check http://localhost:5000/health"
}

Write-Host 'Restore finished.'
