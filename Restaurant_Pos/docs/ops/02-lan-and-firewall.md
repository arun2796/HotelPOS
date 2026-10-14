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

## 5. HTTPS (optional)

Plain HTTP is acceptable on an isolated staff network. Use HTTPS when anyone else can join the network:

1. Create a certificate for the server's address (an internal CA, or a self-signed one with
   `New-SelfSignedCertificate -DnsName hotelpos-server, 192.168.1.100 -CertStoreLocation Cert:\LocalMachine\My`),
   export it with its private key to a `.pfx` and the public part to a `.cer`.
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
4. Install the `.cer` on each terminal (`HotelPOS-Client-Setup.exe /CERT=\\server\share\hotelpos.cer`) and change
   the terminal's server address to `https://<server>:5001` (Administration → This Terminal).
