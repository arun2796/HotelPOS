<#
.SYNOPSIS
  Creates the HTTPS certificate for the HotelPOS server on a restaurant LAN (Phase 18, browser clients).

.DESCRIPTION
  Phones and tablets open the browser client at https://<server>:5001/app. A PWA (home-screen install, offline
  cache) only works over HTTPS, so the server needs a certificate the devices trust. Without internet or DNS the
  practical way is an internal root CA:

    1. a root CA certificate "HotelPOS Root CA" (10 years) — its public part (.cer) is installed once on every
       phone, tablet and Windows terminal;
    2. a server certificate for the server's names/addresses (2 years), signed by that CA, exported with its private
       key to a .pfx that Kestrel loads (appsettings.Production.json → Kestrel:Endpoints:Https:Certificate).

  Run as Administrator on the server PC. Re-run with the same -OutputFolder to renew the server certificate; the CA
  is reused when it already exists in the LocalMachine\Root store.

.EXAMPLE
  .\new-lan-certificate.ps1 -DnsNames hotelpos-server,192.168.1.100 -PfxPassword (Read-Host -AsSecureString 'PFX password')
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string[]] $DnsNames,
    [Parameter(Mandatory)] [securestring] $PfxPassword,
    [string] $OutputFolder = 'C:\ProgramData\HotelPOS\Api',
    [string] $CaName = 'HotelPOS Root CA'
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $OutputFolder | Out-Null

$ca = Get-ChildItem Cert:\LocalMachine\Root | Where-Object { $_.Subject -eq "CN=$CaName" } | Sort-Object NotAfter -Descending | Select-Object -First 1
if (-not $ca) {
    Write-Host "Creating root CA '$CaName'..."
    $ca = New-SelfSignedCertificate -Type Custom -KeyUsage CertSign, CRLSign, DigitalSignature `
        -Subject "CN=$CaName" -KeyExportPolicy Exportable -KeyLength 4096 -HashAlgorithm SHA256 `
        -NotAfter (Get-Date).AddYears(10) -CertStoreLocation Cert:\LocalMachine\My `
        -TextExtension @('2.5.29.19={critical}{text}ca=true&pathlength=0')
    # Trust it on this PC and keep the public part for the devices.
    $rootStore = New-Object System.Security.Cryptography.X509Certificates.X509Store('Root', 'LocalMachine')
    $rootStore.Open('ReadWrite'); $rootStore.Add($ca); $rootStore.Close()
}
$caCer = Join-Path $OutputFolder 'hotelpos-root-ca.cer'
Export-Certificate -Cert $ca -FilePath $caCer -Force | Out-Null

Write-Host "Creating server certificate for $($DnsNames -join ', ')..."
$signer = Get-ChildItem Cert:\LocalMachine\My | Where-Object { $_.Thumbprint -eq $ca.Thumbprint }
if (-not $signer) { throw "The CA's private key is not on this PC; create the server certificate where the CA was made." }
$server = New-SelfSignedCertificate -Type SSLServerAuthentication -DnsName $DnsNames -Signer $signer `
    -KeyExportPolicy Exportable -KeyLength 2048 -HashAlgorithm SHA256 -NotAfter (Get-Date).AddYears(2) `
    -CertStoreLocation Cert:\LocalMachine\My -FriendlyName 'HotelPOS API (LAN)'

$pfx = Join-Path $OutputFolder 'hotelpos.pfx'
Export-PfxCertificate -Cert $server -FilePath $pfx -Password $PfxPassword -Force | Out-Null

Write-Host ''
Write-Host "Server certificate : $pfx  (thumbprint $($server.Thumbprint), valid until $($server.NotAfter.ToString('yyyy-MM-dd')))"
Write-Host "Root CA for devices: $caCer"
Write-Host ''
Write-Host 'Next: add the Kestrel Https endpoint to appsettings.Production.json (docs/ops/02 § 5), open port 5001'
Write-Host '(create-firewall-rule.ps1 -Port 5000 -Https), restart the HotelPOS API service, and install the .cer on'
Write-Host 'every phone, tablet and terminal (Android: Settings > Security > Install certificate > CA certificate;'
Write-Host 'iPhone/iPad: open the file, Settings > Profile Downloaded > Install, then Settings > General > About >'
Write-Host 'Certificate Trust Settings; Windows: HotelPOS-Client-Setup.exe /CERT=<path> or certutil -addstore Root).'
