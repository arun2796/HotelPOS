<#
.SYNOPSIS
    Takes a HotelPOS database backup directly with pg_dump, for when the API (and its Backup now button) is down,
    e.g. right before an upgrade. Same format and naming as the API's backups, verified with pg_restore --list.

.EXAMPLE
    .\backup-now.ps1
.EXAMPLE
    .\backup-now.ps1 -Folder 'E:\HotelPOS-USB' -Username postgres
#>
param(
    [string] $Folder = 'C:\ProgramData\HotelPOS\Backups',
    [string] $Database = 'hotelpos',
    [string] $DbHost = 'localhost',
    [int] $Port = 5432,
    [string] $Username = 'hotelpos',
    [string] $PgBin,
    [string] $MediaPath = 'C:\ProgramData\HotelPOS\Api\media'
)

$ErrorActionPreference = 'Stop'

function Find-PgTool([string] $name) {
    if ($PgBin) { return Join-Path $PgBin "$name.exe" }
    $onPath = Get-Command $name -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    $installed = Get-ChildItem 'C:\Program Files\PostgreSQL' -Directory -ErrorAction SilentlyContinue |
        Sort-Object { [int]($_.Name -replace '\D', '0') } -Descending |
        ForEach-Object { Join-Path $_.FullName "bin\$name.exe" } | Where-Object { Test-Path $_ } | Select-Object -First 1
    if ($installed) { return $installed }
    throw "$name was not found. Pass -PgBin with the PostgreSQL bin folder."
}

if (-not $env:PGPASSWORD) {
    $secure = Read-Host "Password of PostgreSQL user '$Username'" -AsSecureString
    $env:PGPASSWORD = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
}

New-Item -ItemType Directory -Force -Path $Folder | Out-Null
$name = 'hotelpos-{0:yyyyMMdd-HHmmss}' -f (Get-Date)
$dump = Join-Path $Folder "$name.dump"

& (Find-PgTool 'pg_dump') --format=custom --no-owner --file $dump --host $DbHost --port $Port --username $Username --dbname $Database
if ($LASTEXITCODE -ne 0) { Remove-Item $dump -ErrorAction SilentlyContinue; throw "pg_dump failed with exit code $LASTEXITCODE." }

$listing = & (Find-PgTool 'pg_restore') --list $dump
if ($LASTEXITCODE -ne 0 -or -not ($listing -match 'TABLE DATA')) { throw "The backup $dump could not be verified." }

$menu = Join-Path $MediaPath 'menu'
if ((Test-Path $menu) -and (Get-ChildItem $menu -File | Select-Object -First 1)) {
    Compress-Archive -Path (Join-Path $menu '*') -DestinationPath (Join-Path $Folder "$name-media.zip")
}

Write-Host "Backup written and verified: $dump"
