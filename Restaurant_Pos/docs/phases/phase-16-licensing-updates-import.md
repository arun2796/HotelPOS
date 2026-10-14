# Phase 16 — Licensing, Auto-update and Import

Status: Not started · Depends on: Phase 10 · Reference: BillingHub `LicenseService`, `Shared/Licensing`,
`BillingSoftware.LicenseGenerator(.Ui)`, `VelopackUpdateService`, `ImportService`

## 1. Goal

Sell and maintain HotelPOS like BillingHub: an offline, machine-locked licence on the server (with the licensed
number of terminals and a trial), terminals that update themselves to the server's version, and Excel/CSV import of
the menu and customers with preview and undo.

## 3. Scope

**In**
- Licence: ECDSA-signed key bound to the server's machine id; edition, terminal count, expiry; 7-day trial;
  vendor key generator (console + small UI) kept outside the shipped product. Over the limit, extra terminals are
  refused at login with a clear message.
- Auto-update: Velopack for the desktop client; the server hosts the release feed so terminals update over the LAN
  (no internet needed); ties into the Phase 10 version gate.
- Import: menu (categories, items, prices, taxes, stations, modifiers) and customers from Excel/CSV with column
  mapping, validation preview, apply in one transaction, undo of the last import.

**Out**
- Online licence activation, payment collection.

## 6. API endpoints

`/api/license` (status, apply key), `/updates/*` (release feed), `/api/import` (preview, apply, undo).

## 12. Acceptance criteria

- [ ] Trial → licensed key on a clean server; terminal limit enforced; client auto-updates from the server.
- [ ] Menu import of 500 items with preview and undo; Definition of Done satisfied.

## 14. Changes during implementation

(fill in while building)
