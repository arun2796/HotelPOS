# Phase 15 — GST Reports and Period Close

Status: Not started · Depends on: Phases 13, 14 · Reference: BillingHub `Core/Features/Gst`, `PeriodLockBehavior`,
`Reports/PeriodPack`

## 1. Goal

Produce the GST returns a restaurant files (GSTR-1 with JSON export for the portal, GSTR-3B summary, HSN/SAC
summary) straight from settled bills, support B2B bills with the customer's GSTIN, and close months and years so
filed periods can no longer change.

## 3. Scope

**In**
- Customer GSTIN, legal name and state on a bill (validated: checksum, state code); B2B vs B2C classification;
  place of supply; CGST+SGST vs IGST.
- Restaurant service defaults: SAC 996331, rate per tax setting (e.g. 5 % without ITC); per-item overrides.
- GSTR-1 sections (B2B, B2CL, B2CS, HSN/SAC summary, documents issued, credit/debit notes for refunds) with
  validation report and portal JSON; GSTR-3B summary; Excel/PDF via Phase 14.
- Month and year close: pre-checks, period pack, lock (no new, edited, voided or refunded bills dated in it;
  corrections go into the open period). Reopen by Admin with reason.

**Out**
- Direct GSP/e-invoice IRN upload (documented as future integration).

## 5. Data model

`Bills` gain customer GSTIN/legal name/state, place of supply, supply type; `PeriodCloses`. Migration
`Phase15_Gst`.

## 6. API endpoints

`/api/gst/gstr1?period=`, `/gstr1/json`, `/gstr3b`, `/hsn-summary`, `/api/periods` (close, reopen, status).

## 12. Acceptance criteria

- [ ] GSTR-1 JSON for a month passes the offline tool's validation; totals match the sales reports.
- [ ] Closed periods reject changes; Definition of Done satisfied.

## 14. Changes during implementation

(fill in while building)
