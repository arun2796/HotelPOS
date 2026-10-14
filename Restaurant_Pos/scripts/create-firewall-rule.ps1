<#
.SYNOPSIS
    Opens the HotelPOS API port(s) on the server for the Private network profile only, and makes sure PostgreSQL's
    port is not reachable from other PCs. Run in an elevated PowerShell. Safe to run again (it replaces its rules).

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

Get-NetFirewallRule -Group $group -ErrorAction SilentlyContinue | Remove-NetFirewallRule

New-NetFirewallRule -DisplayName "HotelPOS API (TCP $Port)" -Group $group -Direction Inbound -Protocol TCP `
    -LocalPort $Port -Action Allow -Profile Private | Out-Null

if ($Https) {
    New-NetFirewallRule -DisplayName "HotelPOS API HTTPS (TCP $HttpsPort)" -Group $group -Direction Inbound -Protocol TCP `
        -LocalPort $HttpsPort -Action Allow -Profile Private | Out-Null
}

# Clients never talk to the database directly (golden rule 1); block the port in case an installer opened it.
New-NetFirewallRule -DisplayName 'HotelPOS: block PostgreSQL from the network (TCP 5432)' -Group $group -Direction Inbound `
    -Protocol TCP -LocalPort 5432 -RemoteAddress Any -Action Block -Profile Any | Out-Null

$network = (Get-NetConnectionProfile | Select-Object -First 1)
if ($network -and $network.NetworkCategory -ne 'Private') {
    Write-Warning "The active network '$($network.Name)' is $($network.NetworkCategory). Set it to Private, or clients cannot connect:"
    Write-Warning "  Set-NetConnectionProfile -InterfaceIndex $($network.InterfaceIndex) -NetworkCategory Private"
}

Get-NetFirewallRule -Group $group | Format-Table DisplayName, Enabled, Profile, Action -AutoSize
