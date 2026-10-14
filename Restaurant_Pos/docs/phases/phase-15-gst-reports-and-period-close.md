# Phase 15 — GST Reports and Period Close

Status: In progress (started 2026-10-15) · Depends on: Phases 13, 14 · Reference: BillingHub `Core/Features/Gst`, `PeriodLockBehavior`,
`Reports/PeriodPack`

## 1. Goal

Produce the GST returns a restaurant files (GSTR-1 with JSON export for the portal, GSTR-3B summary, HSN/SAC
summary) straight from settled bills, support B2B bills with the customer's GSTIN, and close months and years so
filed periods can no longer change.

## 2. Prerequisites

- Phase 6 (bills), Phase 8 (reports, business day), Phase 9 (permissions, audit), Phase 12 (CQRS pipeline), Phase 13
  (Day End and the closed-day lock `IBusinessDayLock`), Phase 14 (Excel/PDF exporters, letterhead, `DefaultSacCode`).

## 3. Scope

**In**
- Customer GSTIN (format + checksum, state from the first two digits), legal name and state on a bill; place of
  supply; supply type B2B / B2CL / B2CS fixed when the bill is finalized; CGST+SGST (intra-state) or IGST
  (inter-state) from the restaurant's GSTIN state versus the place of supply.
- Restaurant defaults: SAC `996331` (`DefaultSacCode`), per-item HSN/SAC override on menu items.
- GSTR-1 for a month: B2B, B2CL, B2CS, credit notes (cdnr / cdnur) for refunds, HSN/SAC summary, documents issued
  (invoice ranges, cancelled); validation report; portal offline-tool JSON. GSTR-3B summary (3.1(a), 3.1(c), 3.2).
  Excel and PDF through the Phase 14 exporters.
- Month and year close: pre-checks, the lock (`PERIOD_CLOSED`), a period pack beside the backups, reopen by an
  administrator with a reason, all audited.

**Out**
- Direct GSP / e-invoice (IRN) upload: documented as a future integration. Reverse charge, exports, SEZ, amendments
  (b2ba etc.) and GSTR-2B reconciliation are not covered; every invoice is reported `rchrg = N`, `inv_typ = R`.

## 4. Deliverables by project

| Project | Deliverables |
|---|---|
| Domain | `Gst/Gstin` (format + checksum + state), `GstStates`, `GstSupply.Classify`, `GstSplit`, `PeriodClose`, `Periods` (keys, financial year); `Bill` GST fields and `StampGst`; `MenuItem.HsnSac` |
| Application | `Features/Gst` (period data loader, GSTR-1 / 3B / HSN / validation builders, portal JSON, exports; 6 queries), `Features/PeriodClosing` (`ClosePeriodCommand`, `ReopenPeriodCommand`, status and pre-check queries); `IBusinessDayLock` extended with the month lock; `SetBillCustomer` / `Finalize` stamp the supply type |
| Infrastructure | EF configuration, migration `Phase15_Gst` (also grants the new permissions to existing roles), `BusinessDayLock` month locks, `DayEndPackWriter.WritePeriodAsync`, setting `GstB2clThreshold` |
| Api | `GstController`, `PeriodsController` (thin, `Send`); error code `PERIOD_CLOSED` → 409 |
| Contracts | GST and period DTOs, `GstSupplyType`, permissions `gst.view` / `period.close`, setting key |
| Desktop | Bill customer panel (GSTIN, legal name, state, place of supply, validated), item HSN/SAC field, GST hub (GSTR-1 / GSTR-3B / HSN summary / Validation tabs), Period Close screen |
| Tests | GSTIN validator, classification and tax split, GSTR-1 JSON shape and reconciliation, period lock, close/reopen/audit, API contract + authorization matrix, desktop VM and rendering tests |

## 5. Data model

Migration `Phase15_Gst`.

- `Bills` gain `CustomerLegalName` (200), `CustomerStateCode` (char 2), `PlaceOfSupply` (char 2), `SupplyType`
  (B2B/B2CL/B2CS, null until finalized and on older bills, which are classified when reported). `CustomerGstin`
  already exists (Phase 6) and is now validated.
- `MenuItems.HsnSac` (nullable, 4–8 digits; null uses `DefaultSacCode`).
- `PeriodCloses` (Id, Kind Month|Year, PeriodStart, PeriodEnd, Status Closed|Reopened, ClosedAt/By, Checks JSON,
  PackFolder, ReopenedAt/By, ReopenReason, RowVersion). Unique (Kind, PeriodStart). Month key `yyyy-MM`; a year is
  the Indian financial year (1 April – 31 March) keyed by its first calendar year (`2026` = Apr 2026 – Mar 2027).
- Setting `GstB2clThreshold` (decimal, default 100000).

## 6. API endpoints

`period` is `yyyy-MM` (the previous month when omitted). `format=xlsx|pdf` on the report endpoints downloads the same
figures; `/gstr1/json` downloads the portal file.

| Method | Route | Permission |
|---|---|---|
| GET | `/api/gst/gstr1?period=` (B2B, B2CL, B2CS, credit notes, HSN, documents, totals) | `gst.view` |
| GET | `/api/gst/gstr1/json?period=` (`GSTR1_<gstin>_<MMYYYY>.json`) | `gst.view` |
| GET | `/api/gst/gstr3b?period=` | `gst.view` |
| GET | `/api/gst/hsn-summary?period=` | `gst.view` |
| GET | `/api/gst/validation?period=` | `gst.view` |
| GET | `/api/periods?financialYear=` (12 months + the year, with status) | `gst.view` |
| GET | `/api/periods/precheck?kind=&period=` | `period.close` |
| POST | `/api/periods/close` `{kind, period}` | `period.close` |
| POST | `/api/periods/reopen` `{kind, period, reason}` | `period.close` |

New permissions: `gst.view` (Manager, Admin), `period.close` (Admin only; reopen is the same permission). Granted to
existing roles by the migration (the Phase 13 pattern). Bill customer details keep `billing.operate`.

## 7. Real-time events

None. A closed period is read by the next command that needs it.

## 8. Business rules

- **GSTIN**: 15 characters, `^\d{2}[A-Z]{5}\d{4}[A-Z][1-9A-Z]Z[0-9A-Z]$`, a known state code (01–38, 97, 99) and the
  GSTN check character (modulus 36, weights 1/2). The customer state defaults to the GSTIN's.
- **Place of supply** defaults to the restaurant's own state (restaurant service is supplied where it is performed);
  the cashier may choose another state (catering, events). Inter-state = restaurant GSTIN state ≠ place of supply:
  IGST, otherwise CGST + SGST (central half rounded, state half the remainder, as on the invoice).
- **Supply type** (`GstSupply.Classify`): valid customer GSTIN → B2B; else inter-state and invoice value
  **above the threshold → B2CL**; else B2CS. The B2CL limit for inter-state B2C invoices was ₹2,50,000 and was
  reduced to **₹1,00,000 from 1 August 2024** (Notification 12/2024-CT); the setting defaults to the current
  ₹1,00,000 and can be changed if the rule moves again. Set when the bill is finalized and when the customer changes.
- **Period** of a sale = the business day it was *settled* (as the sales reports and the Phase 13 lock count it);
  of a credit note = the day of the refund; of a cancelled invoice = the day it was voided. Invoice date in the return
  is the settled business day.
- **Credit notes**: each refund of a settled bill is a credit note `CN-<refund id>` for the share of the bill it
  returns (value ÷ bill total applied to each rate group). Registered buyer → cdnr; B2CL → cdnur; B2CS refunds reduce
  the B2CS rows.
- **HSN/SAC summary** groups invoice lines by code and rate (item override, else `DefaultSacCode`); it is not net of
  credit notes (quantity cannot be apportioned), so adjust on the portal if refunds are material. UQC is `NA`.
- **Documents issued**: per invoice-number series (the number without its trailing digits) first/last, total,
  cancelled, net; credit notes are document type 5.
- **Validation** (errors block closing, warnings do not): restaurant GSTIN missing/invalid (error), settled bill
  without invoice number (error), return invoice value or tax ≠ the sales report for the month (error), unsettled
  bills (warning), customer GSTIN invalid (warning, reported as B2C), customer state ≠ GSTIN state (warning).
- **Close pre-checks**: month: the month has ended; every business day with a sale or payment is closed by Day End;
  no open or unpaid bill; no validation error. Year: it has ended and all twelve months are closed.
- **Lock**: `IBusinessDayLock.EnterAsync` (the one pipeline behaviour of Phase 13) also takes a shared PostgreSQL
  advisory lock per month and refuses with `PERIOD_CLOSED` when a closed month or year contains the day. Close and
  reopen take the months' locks exclusively, so a payment in flight finishes first or is refused. Reopening a Day End
  inside a closed period is refused too. Corrections go into the open period (the refund is dated today).
- **Reopen**: administrators only, reason required, audited (`Period.Reopened`); a month inside a closed year needs
  the year reopened first. Closing again replaces the pack (a new folder; the audit trail keeps every close).
- **Pack**: `period-<yyyy-MM>-<time>` beside the backups with `gstr1-<MMYYYY>.json`, `gst-<yyyy-MM>.xlsx` and `.pdf`
  (GSTR-1 sections, GSTR-3B, validation); a year gets a month-by-month summary workbook and PDF. A pack that cannot be
  written leaves the period closed with a warning.
- Money is `decimal`; amounts in the JSON are rounded to two decimals.

## 9. Desktop screens

- **Bill customer panel** (billing): GSTIN (validated as typed), legal name, customer state, place of supply.
- **Menu item editor**: optional HSN/SAC.
- **GST hub** (managers): month picker, tabs GSTR-1 (sections as grids) / GSTR-3B / HSN summary / Validation,
  Export JSON / Excel / PDF.
- **Period Close** (admins): the financial year's months and the year with status; close with the pre-check list,
  reopen with a reason.

## 10. Tests

- Domain: GSTIN valid/invalid checksum, format and state; classification (B2B, B2CL above and at the threshold,
  intra-state never B2CL); CGST/SGST/IGST split; period keys and financial year.
- Application: GSTR-1 totals reconcile with the sales report; B2B/B2CL/B2CS placement; refund → credit note; JSON
  shape (`fp`, `b2b`, `b2cs`, `cdnr`, `hsn`, `doc_issue`) and rounding; close pre-checks; locked month rejects new
  bills, payments, voids and refunds with `PERIOD_CLOSED`; reopen allows them, audited.
- API: contract, permissions (matrix), downloads. Desktop: customer panel and hub VMs; rendering screens.

## 11. Manual demo script

1. Settings: restaurant GSTIN. Bill table 1 with a customer GSTIN (B2B) and table 2 as a walk-in; settle both.
2. GST hub for the month: the B2B invoice, the B2CS row, totals equal the sales report; Export JSON.
3. Day End every day; Period Close: close the month; refund one of its bills: refused (`PERIOD_CLOSED`).
4. Admin reopens with a reason; the refund goes through as a credit note in the open month.

## 12. Acceptance criteria

- [ ] GSTR-1 JSON for a month passes the offline tool validation (not verifiable here: no portal tool).
- [x] GSTR-1 totals match the sales reports (tested, and checked by the validation report).
- [x] Closed periods reject changes (`PERIOD_CLOSED`), reopen is Admin-only, with a reason, audited.
- [ ] Definition of Done satisfied (manual demo § 11 still to run on two machines).

## 13. Risks and notes

- The portal schema evolves; field names follow the offline tool (v3) and `version` is informational. Validate a real
  file in the offline tool before relying on it.
- Aggregate turnover (`gt`, `cur_gt`) is not known to the POS and is exported as 0; enter it in the tool.
- Locking touches every bill and payment command: it stays in the one pipeline behaviour.

## 14. Changes during implementation

**Built (2026-10-15, branch `phase-11-ui-design-system`)**

- Contracts/Domain: `Gstin` (+ `GstStates`) in Contracts so the desktop validates while typing; `GstSupply.Classify`,
  `GstSplit`, `PeriodClose`, `Periods` in Domain; `Bill` gets legal name, customer state, place of supply, supply type
  (`StampGst`, called on customer change and at finalize); `MenuItem.HsnSac`. Migration `Phase15_Gst` (also grants
  `gst.view` to Admin and Manager, `period.close` to Admin). Setting `GstB2clThreshold`.
- Application: `Features/Gst` (`GstDataLoader` reads a month once; `GstReports` builds GSTR-1, 3B, HSN, validation and the
  portal JSON; `GstExports` builds Excel/PDF and the pack), `Features/PeriodClosing` (`ClosePeriodCommand` = plain request
  like Day End: `CloseGstPeriodCommand` transaction + pack + `RecordPeriodPackCommand`; `ReopenPeriodCommand`; status
  and pre-check queries). `IBusinessDayLock` gained `CheckPeriodAsync` / `LockMonthsExclusiveAsync`; `EnterAsync` (the one
  pipeline behaviour) now also refuses with `PERIOD_CLOSED`; `ReopenDay` checks it too. `IDayEndPackWriter.WritePeriodAsync`.
- Api: `GstController`, `PeriodsController`; `PERIOD_CLOSED` → 409. Authorization matrix: 9 added lines, nothing else
  changed: `api/gst/gstr1`, `/gstr1/json`, `/gstr3b`, `/hsn-summary`, `/validation` and `api/periods` (GET) are `AM---`;
  `api/periods/precheck` (GET), `/close` (POST), `/reopen` (POST) are `A----`.
- Desktop: customer panel (GSTIN checked as typed, legal name, state, place of supply), item HSN/SAC field, **Tax Returns**
  hub and **Period Close** screens (registered, permission-gated); `ScreenRenderingTests` now renders 67 screens, no
  binding errors.
- Tests added: 35 domain, 18 application (`Application.Tests/Gst`), 2 API, 4 desktop (+ 2 rendering screens). Full run: Domain 350,
  Api 172, Desktop 211 (1 skipped, pre-existing) all green; Application 206 green when run alone before the last refactor.

**Decisions and deviations**

- **B2CL threshold is ₹1,00,000, not ₹2,50,000.** The limit for inter-state invoices to unregistered buyers was ₹2.5 lakh
  until 31 July 2024 and became ₹1 lakh from 1 August 2024 (Notification 12/2024-CT). It is the setting
  `GstB2clThreshold` (default 100000); "above" is strict (an invoice of exactly the limit is B2CS). Please confirm
  against the current GSTR-1 instructions.
- Place of supply defaults to the restaurant's own state (restaurant service is supplied where it is performed), so
  IGST / B2CL only appear when the cashier picks another state. The customer state is informational (a mismatch with
  the GSTIN state is a validation warning).
- Sales are dated by the business day of `SettledAt` (as the sales reports and the Phase 13 lock do), refunds by the day of
  the refund. An invoice finalized in one month and paid in the next is therefore reported in the second.
- JSON choices (offline tool v3 field names): `fp` MMYYYY, `gt`/`cur_gt` exported as 0 (the POS does not know the
  aggregate turnover), `version` `GST3.1.6`, `hash` `"hash"`, item `num` = rate × 100 + 1, only the tax heads that apply
  (`iamt` or `camt`+`samt`, plus `csamt` 0), `rchrg` N and `inv_typ` R for every invoice, HSN rows with `uqc` NA and `rt`,
  credit notes without the original invoice number (the v3 `cdnr` has none), empty sections omitted, `doc_issue` types 1
  (invoices) and 5 (credit notes). Not validated in the real offline tool (no portal tool here).
- The close writes no backup of its own (Day End already took one per day); the pack is the evidence. No hub events.
- `Tax Returns` (not "GST Returns") is the sidebar title so the command palette keeps ranking Restaurant Settings first
  for "gst" (existing ranking test unchanged).
- HSN totals are gross of credit notes; HSN splits are computed per code/rate group and may differ from the invoice
  split by rounding paise.

**Not done / deferred**

- GSP / e-invoice IRN upload; amendments, reverse charge, exports/SEZ, GSTR-2B and ITC tables of the 3B.
- Manual demo (§ 11) and validating the JSON in the GST offline tool.
- Per-month event to refresh open terminals when a period closes (the next command reports `PERIOD_CLOSED`).
