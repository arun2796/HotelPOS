# 08 — Licence

HotelPOS runs on a licence that belongs to the **server PC**. A new server starts with a **7-day trial** (up to 10
terminals); after that a licence key is needed. Keys work offline, are tied to one PC, and say how many terminals may
sign in and until when.

## 1. Apply a licence key

1. On the server (or any terminal) sign in as **admin** and open **Administration → Licence**.
2. Copy the **machine id** (Copy button) and send it to your supplier with the number of terminals you need.
3. Paste the key you receive (it starts with `HPOS1-`) into **Apply a licence key** and press **APPLY KEY**.
   It takes effect at once: no restart. The page then shows Licensed, the edition, terminals and the valid-until date.

If the key is refused the message says why: *not genuine* (copied incorrectly: copy it again), *issued for another
PC* (the machine id on the order was from a different PC), or *expired*.

## 2. What happens at the limits

- **More terminals than licensed:** the terminal that would be over the limit cannot sign in and sees a message
  ("This licence covers N terminal(s)…"). Terminals are counted in the order they were first registered. Deactivate
  terminals that are no longer used under **Administration → Devices**, or ask for a larger licence.
- **Trial ended or licence expired:** HotelPOS becomes **read-only**. Everything can still be viewed and exported, but
  no order, payment or setting can be saved, and only administrators can sign in. Apply a new key to continue.
  Backups can still be taken. A banner on every screen appears from 14 days before expiry (and during the trial).

## 3. Moving the server to another PC

A key is tied to the PC. For a new server PC send the new machine id to your supplier and apply the new key after
restoring the backup. A database restored onto a different PC without a new key is treated as unlicensed (read-only
once its trial date has passed).

## 4. For the supplier (vendor tool)

`tools/HotelPOS.LicenseGenerator` (never shipped, never installed at the restaurant):

```
HotelPOS.LicenseGenerator keygen --out C:\vendor\license-private.pem        (once; prints the public key)
HotelPOS.LicenseGenerator issue --private-key C:\vendor\license-private.pem --machine HP-... \
    --customer "Hotel Saravana" --terminals 5 --expires 2027-10-31          (omit --expires: perpetual)
HotelPOS.LicenseGenerator inspect --public-key <base64> <key>
```

`keygen` refuses to write inside a git repository. The public key it prints replaces `EmbeddedLicenseKey.PublicKey`
in `src/HotelPOS.Application/Features/Licensing/LicenseService.cs` before the release is built. **Keep the private key
safe and backed up outside the repository**: without it no more keys can be issued for builds that carry this public key.
