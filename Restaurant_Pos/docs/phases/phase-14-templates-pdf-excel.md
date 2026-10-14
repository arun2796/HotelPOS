# Phase 14 — Print Templates, PDF and Excel

Status: In progress (started 2026-10-14) · Depends on: Phase 12 · Reference: BillingHub `Core/Printing/BillRenderModel`,
`TemplateDesignerViewModel` (Template Studio), QuestPDF, ClosedXML, print log

## 1. Goal

Every printed or exported document comes from one server-built render model and the restaurant's own template:
thermal ESC/POS, Windows printers and screen preview (as today), plus PDF. The owner adjusts invoice, receipt and
KOT layouts in a Template Studio with a live preview, without a developer. Every report, the audit log and the Day
End pack export to Excel and PDF with the restaurant's letterhead. A print log counts copies, so every reprint says
DUPLICATE.

## 2. Prerequisites

- Phase 7 (server-built `KitchenTicketDocument`, `InvoiceDocument`, `ReceiptDocument`; desktop `EscPosRenderer`,
  `FlowDocumentRenderer`, print queue; `/api/print/*`, reprint audit), Phase 8 (reports and `ReportSchema`), Phase 12
  (CQRS). Phase 13 adds X/Z reports and the Day End pack as further document types.
- Decision needed before go-live: QuestPDF Community licence (free below USD 1 M annual gross revenue) or
  commercial. Recorded in the go-live checklist.

## 3. Scope

**In**
- **Render model**: the Phase 7 document DTOs become the render model, extended with `CopyNumber` / `CopyLabel`
  ("DUPLICATE" from the second copy), the template in force, an optional UPI payment QR payload
  (`upi://pay?pa=…&am=…&tn=<invoice>`), and logo/letterhead references. New types: X report, Z report, Day End
  summary (Phase 13).
- **Templates** per document type (KOT, invoice, receipt, X/Z report): paper (58 mm, 80 mm, A5, A4); logo on/off;
  header lines; footer lines and terms; column set and order (item, HSN/SAC, qty, rate, discount, tax %, amount);
  GST breakup style (CGST/SGST rows, a rate-wise table, or a single tax line); show UPI QR; font size (thermal:
  normal/large); KOT options (prices, notes in bold, station heading). Versioned; one active per type; activating a
  version is audited; a change applies from the next print.
- **Template Studio** (desktop, Admin/Manager): options on the left, live preview on the right using sample data or
  a chosen real bill, "Print test" to any configured printer, save as new version, activate, roll back.
- **PDF** (server, QuestPDF): invoice and receipt (A4, A5, 80 mm roll), every report, X/Z report, Day End pack.
  `GET /api/print/{type}/{id}.pdf`.
- **Excel** (server, ClosedXML): every report grid (same columns as the CSV and the screen), the audit log export,
  the Day End pack workbook (one sheet per report). `format=xlsx` on report and audit endpoints; CSV stays.
- **Print log**: every print or PDF of a document is logged (type, id, copy number, printer or "PDF", terminal,
  user, time, result). Copy 1 is the original; later copies are DUPLICATE (`PrintedCopy` audit). Reprint screens show
  the history.
- **Optional (stretch)**: e-mail an invoice PDF to the customer (SMTP settings, audited). Only if the owner asks.

**Out**
- Free-form drag-and-drop designers (the column/section model covers restaurant documents).
- Printing PDFs to thermal printers (thermal stays ESC/POS).

## 4. Deliverables by project

| Project | Deliverables |
|---|---|
| Domain | `DocumentTemplate` (type, version, layout, active), `TemplateLayout` value object with validation, `PrintLogEntry`, copy-number rule |
| Application | `Features/Templates/*` (List, Get, Save new version, Activate, Preview), `Features/Printing/*` (GetDocument with copy reservation, LogPrint, GetPrintHistory), `Features/Exports/*` (report → xlsx/pdf, audit → xlsx); `IPdfRenderer`, `IExcelExporter` abstractions |
| Infrastructure | QuestPDF renderer (letterhead, tables, GST breakup, QR), ClosedXML exporter (typed columns: dates, ₹ number format `#,##,##0.00`), migration `Phase14_Templates`, seed default templates matching today's output |
| Api | `TemplatesController`, print endpoints (`.pdf`, log, history), `format=xlsx|pdf` on reports and audit |
| Contracts | Template DTOs, layout schema, copy fields on documents, `PrintLogDto` |
| Desktop | Template Studio hub page, `EscPosRenderer` and `FlowDocumentRenderer` driven by the template, DUPLICATE banner, "Save PDF" / "Export Excel" buttons on reports, audit, closed bills, Day End |
| Tests | Template validation, copy numbering under concurrency, renderer golden tests (ESC/POS byte streams and PDF text extraction), Excel column/format tests, API matrix, rendering screens |

## 5. Data model

Migration `Phase14_Templates`.

- `DocumentTemplates` (Id, DocumentType, Version, Name, LayoutJson, IsActive, CreatedAt/By, ActivatedAt/By).
  Unique (DocumentType, Version); one active per type.
- `PrintLogs` (Id, DocumentType, DocumentId, CopyNumber, Output Printer|Pdf|Email, PrinterName, DeviceId,
  UserId, CreatedAt, Succeeded, Error, IdempotencyKey). Unique (DocumentType, DocumentId, CopyNumber).
- Settings: `UpiVpa`, `UpiPayeeName`, `LogoFile` (stored like menu images), `PdfPaper` default, `SmtpHost`… (stretch).

## 6. API endpoints

| Method | Route | Permission |
|---|---|---|
| GET | `/api/templates`, `/api/templates/{type}` | `settings.manage` |
| POST | `/api/templates/{type}` (new version) | `settings.manage` |
| POST | `/api/templates/{type}/{version}/activate` | `settings.manage` |
| POST | `/api/templates/preview` (layout + sample or bill id → render model) | `settings.manage` |
| POST | `/api/print/{type}/{id}/copies` (reserve next copy, idempotency key) → document with copy label | as the document today |
| PUT | `/api/print/copies/{logId}` (result: printed / failed) | same |
| GET | `/api/print/{type}/{id}.pdf` | same |
| GET | `/api/print/{type}/{id}/history` | `billing.operate` / `reports.view` |
| GET | `/api/reports/{key}?format=xlsx|pdf` | `reports.view` |
| GET | `/api/audit/export?format=xlsx` | `audit.view` |

The Phase 7 `GET /api/print/{type}/{id}` stays for preview (no copy reserved).

## 7. Real-time events

`TemplateActivated` (all terminals reload the template), `DocumentPrinted` (managers; feeds the print log screens).

## 8. Business rules

- The template only changes presentation, never amounts: totals, tax and invoice numbers come from the persisted bill.
- Copy numbers are allocated by the server in a transaction, so two terminals reprinting at once get copies 2 and 3,
  never two "originals". A failed print keeps its number and is marked failed; the retry takes the next number.
- The invoice PDF is the same document as the printed invoice (same render model, same copy rules).
- GST invoices always show the restaurant GSTIN, the invoice number and date, the HSN/SAC summary when the template
  shows HSN, and the tax breakup. The template can change the style, not remove legally required fields.
- Excel exports use typed cells (dates as dates, amounts as numbers with Indian grouping format) and the same
  columns and totals as the screen and the CSV.

## 9. Desktop screens

- **Template Studio** (Settings hub tab "Print templates"): document type tabs; option groups (paper, header,
  columns, tax, footer, QR, KOT); live preview (thermal width or A4 page) with sample or real bill; version list
  with activate and roll back; "Print test".
- **Reports, Audit Log, Closed Bills, Day End**: "Export Excel" and "Save PDF" next to "Export CSV".
- **Bill and reprint dialogs**: copy history ("Original 12:04 BILLING-01 · Copy 2 12:31 …"); reprints show and print
  DUPLICATE.

## 10. Tests

- Layout validation (unknown columns, required GST fields cannot be hidden on invoices).
- Copy numbering: concurrent reserve on two connections → 2 and 3; failed print keeps its number.
- Golden tests: ESC/POS bytes for a sample invoice per paper width; PDF text extraction contains invoice number,
  GSTIN, totals, DUPLICATE on copy 2; Excel cell types and number formats.
- API: endpoints, matrix, formats. Desktop: Template Studio VM (options → preview model), rendering screens.

## 11. Manual demo script

1. Template Studio: switch the invoice to 58 mm, hide the waiter, add a UPI QR and a footer line; preview with a
   real bill; activate.
2. Settle a bill on a billing terminal: the thermal invoice follows the new template; reprint shows DUPLICATE.
3. Save the invoice as A4 PDF and open it; export Item Sales to Excel and check Indian number formatting.
4. Roll the template back to the previous version; the next print uses it.

## 12. Acceptance criteria

- [x] Invoice, receipt and KOT print from templates on thermal and as PDF; reprints are marked DUPLICATE with a
      server-side copy number.
- [x] Reports, the audit log and the Day End pack export to Excel and PDF.
- [ ] Definition of Done satisfied.

## 13. Risks and notes

- QuestPDF licence (see § 2). ClosedXML is MIT.
- Font for PDFs: embed Inter (OFL) and a Devanagari/Tamil-capable fallback (Noto Sans, OFL) so regional item names
  render correctly.
- ESC/POS printers differ in code pages; keep the Phase 7 `CurrencyFallback` ("Rs.") behaviour per printer profile.

## 14. Changes during implementation


**Built.** Server: versioned `DocumentTemplate`s (KOT/invoice/receipt, activate and roll back, `TemplateActivated`
event), `PrintLog` with a server-side copy number (copy 2 onwards says DUPLICATE; failed prints keep their number),
QuestPDF invoice/receipt/KOT PDFs from the same document model as the thermal print, ClosedXML Excel and PDF exports
of reports and the audit log, UPI QR and logo on invoices. Migrations `Phase14_Templates` (stamped after
`Phase13_CashAndDayEnd`). Desktop: Template Studio as the "Print templates" tab of Settings (options, live preview,
print test, versions), renderers use the active template, DUPLICATE on reprints with copy history, "Export Excel" /
"Save PDF" on Reports and Audit Log, invoice PDF on Closed Bills.

**Decisions.** The preview document is handed to the viewer only while the view is loaded (a FlowDocument can belong to
one viewer). A print copy is owned by the reserving user or terminal; managers can complete any copy. Print tests now
open a shift on the counter because Phase 13 requires one for cash.

**Deferred.** E-mail invoice; Noto Indic fallback font for PDFs (needs font research; regional names fall back to Inter);
Day End pack export (belongs to the Day End screen of Phase 13, hook is the export pipeline); manual demo (§ 11) open.

**Matrix lines added** (14): `api/print/*` copies, invoice/receipt PDFs and history, and `api/templates/*`
(list, type, save, activate, preview, logo).
