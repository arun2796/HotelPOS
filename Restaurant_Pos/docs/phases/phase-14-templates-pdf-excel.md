# Phase 14 — Print Templates, PDF and Excel

Status: Not started · Depends on: Phase 12 · Reference: BillingHub `Core/Printing/BillRenderModel`,
`TemplateDesignerViewModel` (Template Studio), QuestPDF, ClosedXML, print log

## 1. Goal

One render model for every printed or exported document (screen preview, thermal ESC/POS, A4/A5 PDF), a Template
Studio where the owner lays out invoice, receipt and KOT without a developer, PDF and Excel exports of every report
with the restaurant's letterhead, and a print log that marks reprints as DUPLICATE.

## 3. Scope

**In**
- `DocumentRenderModel` built on the server from persisted data (extends Phase 7 documents).
- Templates: paper (58/80 mm, A5, A4), logo, header/footer lines, column set and order, GST breakup style,
  UPI QR for the bill amount, terms; stored per document type, versioned, previewed live.
- PDF (QuestPDF) for invoices and reports; Excel (ClosedXML) for every report grid, the audit log and Day End pack.
- Print log (who, what, which printer, copy number); reprints print "DUPLICATE".
- Optional: e-mail an invoice PDF (SMTP settings).

**Out**
- Arbitrary free-form drag-and-drop designers beyond the column/section model.

## 5. Data model

`DocumentTemplates` (type, name, version, JSON layout, active), `PrintLogs`. Migration `Phase14_Templates`.

## 6. API endpoints

`/api/templates` (list, get, save, activate, preview), `/api/print/pdf/{type}/{id}`, `format=xlsx|pdf` on report
endpoints, `/api/print/log`.

## 12. Acceptance criteria

- [ ] Invoice, receipt and KOT print from templates on thermal and as PDF; reports export to Excel and PDF.
- [ ] Definition of Done satisfied.

## 13. Risks and notes

- QuestPDF Community licence is free below USD 1 M annual revenue; record the licence choice in the go-live notes.

## 14. Changes during implementation

(fill in while building)
