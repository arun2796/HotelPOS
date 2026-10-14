# 07 — Go-live checklist

Tick every item on site before the first real service. Keep the signed copy with the venue's documents.

Venue: ____________________  Date: __________  Technician: ____________________  Version: __________

## Server

- [ ] Server PC on a UPS, wired, fixed address `__________` ([02](02-lan-and-firewall.md) § 1).
- [ ] Network profile is **Private**; firewall rules from `create-firewall-rule.ps1` present.
- [ ] From a terminal: port 5000 reachable, **port 5432 not reachable** (`Test-NetConnection`).
- [ ] PostgreSQL role `hotelpos` owns database `hotelpos`; the `postgres` password is stored off the PC.
- [ ] The release was built with ASP.NET Core **8.0.32 or later** (`version.txt` in the server folder); no
      `NOT-FOR-RELEASE.txt` next to it.
- [ ] Service **HotelPOS API** is Automatic and running; `/health` is Healthy.
- [ ] Server time zone and clock are correct.

## Accounts and security

- [ ] `admin` password changed from the installer's; stored safely.
- [ ] One account per staff member, correct roles; no demo users (`waiter1` etc. must not exist).
- [ ] Roles & Permissions reviewed with the owner (who may void, refund, discount, cancel orders).
- [ ] Every terminal registered with a clear name (Devices); unused ones deactivated.

## Data

- [ ] Restaurant settings complete (name, address, GSTIN, invoice prefix, business-day start, taxes).
- [ ] Menu, prices and taxes checked against the printed menu; stations assigned for the kitchen screens.
- [ ] Sections and tables match the floor.

## Backup and recovery

- [ ] **BACKUP NOW** succeeded; scheduled time set (default 03:30) and enabled.
- [ ] Restore rehearsal done into `hotelpos_restoretest` and the counts matched ([03](03-backup-and-recovery.md) § 6).
- [ ] Off-site copy routine agreed (who, which USB drive, which day).

## Terminals and printing

- [ ] Every terminal: correct server address, signs in, shows 🟢.
- [ ] Kitchen screens bound to their station; a test order reaches the right screen.
- [ ] KOT, invoice and receipt print on the right printers; paper width right.

## Full rehearsal (two terminals at least)

- [ ] Order → kitchen → ready → served → bill → discount → payment (cash and card) → invoice printed.
- [ ] Refund or void with manager approval.
- [ ] Pull the server's network cable for 30 s during an order: terminals show 🔴, then 🟢; no order lost or
      duplicated.
- [ ] Restart the server PC: the service comes back by itself; terminals reconnect.
- [ ] Reports show the rehearsal's sales; then remove the rehearsal data (restore the pre-rehearsal backup or void
      the test bills, as agreed with the owner).

## Handover

- [ ] Manager walked through [06 — Daily operations](06-daily-operations.md).
- [ ] Support contact and this checklist left with the venue.

Signed (venue): ____________________  Signed (technician): ____________________
