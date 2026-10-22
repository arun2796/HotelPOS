<#
.SYNOPSIS
    Creates the PostgreSQL role and database HotelPOS uses, with the postgres superuser password.

.DESCRIPTION
    Run by the server installer (and configure-server.ps1) when the superuser password was entered on the Database
    page, and safe to run by hand. Idempotent: an existing role gets its password set to -DbPassword (so the role and
    the HotelPOS settings always agree), an existing database is left alone. The API itself never uses the superuser.

    psql is taken from -PsqlPath, then PATH, then the newest C:\Program Files\PostgreSQL\<version>\bin.

.EXAMPLE
    .\new-database.ps1 -SuperuserPassword 'pg-admin-pw' -DbPassword 'hotelpos-pw'
#>
[CmdletBinding()]
param(
    [string] $DbHost = 'localhost',
    [int] $DbPort = 5432,
    [string] $Database = 'hotelpos',
    [string] $DbUser = 'hotelpos',
    [Parameter(Mandatory = $true)] [string] $DbPassword,
    [Parameter(Mandatory = $true)] [string] $SuperuserPassword,
    [string] $SuperuserName = 'postgres',
    [string] $PsqlPath
)

$ErrorActionPreference = 'Stop'

function Find-Psql {
    if ($PsqlPath -and (Test-Path $PsqlPath)) { return $PsqlPath }
    $onPath = Get-Command psql.exe -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    $installed = Get-ChildItem "$env:ProgramFiles\PostgreSQL" -Directory -ErrorAction SilentlyContinue |
        Sort-Object { [int]($_.Name -replace '\D', '0') } -Descending |
        ForEach-Object { Join-Path $_.FullName 'bin\psql.exe' } |
        Where-Object { Test-Path $_ } |
        Select-Object -First 1
    if ($installed) { return $installed }
    throw "psql.exe was not found (PATH or $env:ProgramFiles\PostgreSQL\<version>\bin). Install PostgreSQL, or create the role and database by hand (docs\ops\01 § 3)."
}

# The password travels through the environment, never on the command line.
$psql = Find-Psql
$env:PGPASSWORD = $SuperuserPassword
try {
    function Invoke-Sql([string] $sql, [string] $db = 'postgres') {
        # psql reports on stderr; under 'Stop' the 2>&1 merge itself would throw before the exit code is read.
        $previous = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        $output = & $psql -h $DbHost -p $DbPort -U $SuperuserName -d $db -v ON_ERROR_STOP=1 -At -c $sql 2>&1
        $ErrorActionPreference = $previous
        if ($LASTEXITCODE -ne 0) {
            $text = (($output | ForEach-Object { "$_" }) -join "`n").Trim()
            if ($text -match 'password authentication failed') { throw "The postgres superuser password is not correct (PostgreSQL said: $text)." }
            throw "psql failed: $text"
        }
        return ($output | Out-String).Trim()
    }

    # Identifiers quoted, literals with doubled single quotes: names and passwords may contain anything.
    $roleIdent = '"' + $DbUser.Replace('"', '""') + '"'
    $dbIdent = '"' + $Database.Replace('"', '""') + '"'
    $roleLiteral = "'" + $DbUser.Replace("'", "''") + "'"
    $dbLiteral = "'" + $Database.Replace("'", "''") + "'"
    $passwordLiteral = "'" + $DbPassword.Replace("'", "''") + "'"

    if ((Invoke-Sql "SELECT 1 FROM pg_roles WHERE rolname = $roleLiteral") -eq '1') {
        Invoke-Sql "ALTER ROLE $roleIdent WITH LOGIN PASSWORD $passwordLiteral" | Out-Null
        Write-Host "Role $DbUser exists; its password now matches the HotelPOS settings."
    } else {
        Invoke-Sql "CREATE ROLE $roleIdent WITH LOGIN PASSWORD $passwordLiteral" | Out-Null
        Write-Host "Role $DbUser created."
    }

    if ((Invoke-Sql "SELECT 1 FROM pg_database WHERE datname = $dbLiteral") -eq '1') {
        Write-Host "Database $Database exists; left as it is."
    } else {
        Invoke-Sql "CREATE DATABASE $dbIdent OWNER $roleIdent" | Out-Null
        Write-Host "Database $Database created, owned by $DbUser."
    }
} finally {
    Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue
}
