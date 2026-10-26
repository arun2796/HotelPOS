<#
.SYNOPSIS
    Opens the HotelPOS API port(s) on the server for the Private network profile only, and makes sure PostgreSQL's
    port is not reachable from other PCs. Run in an elevated PowerShell. Safe to run again (it replaces its rules).

.DESCRIPTION
    Uses the NetSecurity cmdlets when their WMI provider is available and falls back to netsh advfirewall when it is
    not (Windows Sandbox and some stripped-down editions answer "Invalid class"). When neither works the script warns
    and returns: a missing firewall rule must not stop the server installation, the technician opens the port by hand.

.EXAMPLE
    .\create-firewall-rule.ps1
.EXAMPLE
    .\create-firewall-rule.ps1 -Https
#>
param(
    [int] $Port = 5000,
    [switch] $Https,
    [int] $HttpsPort = 5001
)

$ErrorActionPreference = 'Stop'
$group = 'HotelPOS'
$apiRule = "HotelPOS API (TCP $Port)"
$httpsRule = "HotelPOS API HTTPS (TCP $HttpsPort)"
$blockRule = 'HotelPOS: block PostgreSQL from the network (TCP 5432)'

function Use-NetSecurity {
    Get-NetFirewallRule -Group $group -ErrorAction SilentlyContinue | Remove-NetFirewallRule
    New-NetFirewallRule -DisplayName $apiRule -Group $group -Direction Inbound -Protocol TCP `
        -LocalPort $Port -Action Allow -Profile Private | Out-Null
    if ($Https) {
        New-NetFirewallRule -DisplayName $httpsRule -Group $group -Direction Inbound -Protocol TCP `
            -LocalPort $HttpsPort -Action Allow -Profile Private | Out-Null
    }
    # Clients never talk to the database directly (golden rule 1); block the port in case an installer opened it.
    New-NetFirewallRule -DisplayName $blockRule -Group $group -Direction Inbound `
        -Protocol TCP -LocalPort 5432 -RemoteAddress Any -Action Block -Profile Any | Out-Null

    $network = (Get-NetConnectionProfile | Select-Object -First 1)
    if ($network -and $network.NetworkCategory -ne 'Private') {
        Write-Warning "The active network '$($network.Name)' is $($network.NetworkCategory). Set it to Private, or clients cannot connect:"
        Write-Warning "  Set-NetConnectionProfile -InterfaceIndex $($network.InterfaceIndex) -NetworkCategory Private"
    }
    Get-NetFirewallRule -Group $group | Format-Table DisplayName, Enabled, Profile, Action -AutoSize
}

function Use-Netsh {
    foreach ($name in @($apiRule, $httpsRule, $blockRule)) { & netsh advfirewall firewall delete rule name="$name" | Out-Null }
    & netsh advfirewall firewall add rule name="$apiRule" dir=in action=allow protocol=TCP localport=$Port profile=private | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "netsh could not add the rule '$apiRule' (exit code $LASTEXITCODE)." }
    if ($Https) {
        & netsh advfirewall firewall add rule name="$httpsRule" dir=in action=allow protocol=TCP localport=$HttpsPort profile=private | Out-Null
    }
    & netsh advfirewall firewall add rule name="$blockRule" dir=in action=block protocol=TCP localport=5432 profile=any | Out-Null
    & netsh advfirewall firewall show rule name="$apiRule" | Select-String 'Rule Name|Enabled|Profiles|Action' | ForEach-Object { $_.Line.Trim() }
}

try {
    Use-NetSecurity
} catch {
    Write-Warning "Firewall cmdlets unavailable here ($($_.Exception.Message.Trim())); using netsh advfirewall instead."
    try {
        Use-Netsh
    } catch {
        Write-Warning "The firewall rules could not be created: $($_.Exception.Message.Trim())"
        Write-Warning "Open TCP $Port$(if ($Https) { " and $HttpsPort" }) for the Private profile by hand (docs/ops/02-lan-and-firewall.md) and block TCP 5432 from the network."
    }
}
