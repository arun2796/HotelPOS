# 02 — LAN and firewall

HotelPOS runs entirely on the venue's own network. Terminals talk only to the API on the server; nothing talks
to PostgreSQL except the API on the same PC.

```
 tablets / kitchen / billing  ──Wi-Fi / LAN──►  server PC :5000 (HotelPOS API)  ──localhost──►  PostgreSQL :5432
```

## 1. Give the server a fixed address

Terminals store the server address, so it must not change.

- **Preferred:** a DHCP reservation on the router for the server's network card (router admin page →
  DHCP → reserved addresses → the server's MAC address). Nothing to change on Windows.
- **Alternative:** a static IP on Windows (Settings → Network → the adapter → IP assignment → Manual), outside
  the router's DHCP range, with the router as gateway and DNS.
- **Alternative:** if the router resolves PC names, terminals can use `http://<server-name>:5000`
  (e.g. `http://hotelpos-server:5000`). Test from a terminal with `ping hotelpos-server` first.

Wire the server to the router with a cable; Wi-Fi is fine for terminals.

## 2. Network profile

Windows applies the firewall rules to the **Private** profile only. On the server, set the venue network to Private
(Settings → Network → the connection → Network profile type → Private), or in PowerShell:

```powershell
Get-NetConnectionProfile
Set-NetConnectionProfile -InterfaceIndex <index> -NetworkCategory Private
```

## 3. Firewall

The server installer runs `scripts\create-firewall-rule.ps1`. Run it again (elevated) after changing the port or
enabling HTTPS:

```powershell
& 'C:\Program Files\HotelPOS\Server\scripts\create-firewall-rule.ps1' -Port 5000            # HTTP
& 'C:\Program Files\HotelPOS\Server\scripts\create-firewall-rule.ps1' -Port 5000 -Https     # also 5001
```

It allows inbound TCP 5000 (and 5001) on Private networks and **blocks inbound TCP 5432**, so PostgreSQL cannot be
reached from terminals even if another installer opened it. Check from a terminal:

```powershell
Test-NetConnection 192.168.1.100 -Port 5000   # TcpTestSucceeded : True
Test-NetConnection 192.168.1.100 -Port 5432   # TcpTestSucceeded : False  (must be False)
```

## 4. Wi-Fi

- Keep staff terminals on a network guests cannot join (a separate SSID or the router's guest network for guests).
- Use WPA2/WPA3 with a strong key; change it when staff with the key leave.
- Kitchen screens far from the router: add an access point rather than relying on a weak signal. The app survives
  drop-outs (orders are kept and re-sent), but staff notice the delay.

## 5. HTTPS (on by default since Phase 20)

The server listens on both **5000 (plain HTTP, for Windows terminals)** and **5001 (HTTPS)**. The installer / 
`configure-server.ps1` writes the Kestrel endpoints, creates the LAN certificate (or uses the `.pfx` you give with
`-CertificatePath` / `-CertificatePassword`) and opens both ports. On re-runs the existing certificate is kept. HSTS is
sent on the HTTPS endpoint only; there is no redirect from 5000 to 5001, so terminals on plain HTTP keep working.
Phones and tablets use `https://<server>:5001/app` (home-screen install and offline cache only work over HTTPS).
Distributing the root CA to the devices is unchanged (step 4 below). Steps 1-3 are what the installer already did;
they remain here for renewing the certificate or for doing it by hand.

1. Create the certificates with the internal-CA script (run as Administrator on the server):

   ```powershell
   & 'C:\Program Files\HotelPOS\Server\scripts\new-lan-certificate.ps1' -DnsNames hotelpos-server,192.168.1.100 `
       -PfxPassword (Read-Host -AsSecureString 'PFX password')
   ```

   It creates a root CA **HotelPOS Root CA** (10 years) and a server certificate signed by it (2 years), writes
   `C:\ProgramData\HotelPOS\Api\hotelpos.pfx` (private key, for Kestrel) and `hotelpos-root-ca.cer` (public part, for
   the devices). Re-run it to renew the server certificate; the CA is reused. (A certificate from another internal
   CA, or a self-signed `New-SelfSignedCertificate -DnsName …`, works the same way.)
2. Add to `C:\Program Files\HotelPOS\Server\appsettings.Production.json`:

   ```json
   "Kestrel": {
     "Endpoints": {
       "Http":  { "Url": "http://0.0.0.0:5000" },
       "Https": { "Url": "https://0.0.0.0:5001", "Certificate": { "Path": "C:\\ProgramData\\HotelPOS\\Api\\hotelpos.pfx", "Password": "<pfx password>" } }
     }
   }
   ```

3. Run the firewall script with `-Https`, restart the **HotelPOS API** service.
4. Install the root CA `.cer` on each device and point it at HTTPS:
   - Windows terminals: `HotelPOS-Client-Setup.exe /CERT=\\server\share\hotelpos-root-ca.cer` (or
     `certutil -addstore Root hotelpos-root-ca.cer`), then Administration → This Terminal → `https://<server>:5001`.
   - Android phones/tablets: copy the `.cer` to the device → Settings → Security → *Install a certificate* →
     *CA certificate*. Then open `https://<server>:5001/app` in Chrome and *Add to Home screen*.
   - iPhone/iPad: open the `.cer` (AirDrop, mail or a web link) → Settings → *Profile Downloaded* → Install → then
     Settings → General → About → *Certificate Trust Settings* → enable full trust. Open `https://<server>:5001/app`
     in Safari → Share → *Add to Home Screen*.
   The browser client is served by the API itself (same address, path `/app`); no extra port or server is needed.
