# Phase 7 — Printing

Status: Done (2026-10-07) · Depends on: Phase 5 (KOT), Phase 6 (invoice, receipt)

## 1. Goal

Print kitchen order tickets, customer invoices and payment receipts on thermal (ESC/POS), network
(TCP 9100) and standard Windows printers, from the client PC that owns the printer, through a
printing service that business code never depends on. Print failures never block orders or
payments.

## 2. Prerequisites

- Phases 5 and 6. Printing design in `docs/05` § 11 and settings in `docs/05` § 3.

## 3. Scope

**In**
- API print-document endpoints that return fully resolved documents (restaurant header, GSTIN, tax
  breakup, footer) so layout data is authoritative.
- Desktop `IPrintService`, renderers (ESC/POS bytes, WPF FlowDocument), channels (RAW spooler, TCP,
  Windows PrintQueue), print queue with retry, printer profiles in `settings.json`, test print.
- Auto-print rules: KOT on kitchen device when a ticket is created (per station), invoice on
  finalize, receipt on settlement; manual reprint with audit.
- Print preview for Windows printers; admin/manager printer settings screen (per device).

**Out**
- Cash drawer kick (add ESC/POS pulse later via the same channel), label printers, PDF export
  (optional later via the FlowDocument renderer), server-side printing.

## 4. Deliverables by project

| Project | Deliverables |
|---|---|
| Contracts | `KitchenTicketDocument`, `InvoiceDocument` (header, lines, discount, tax breakup rows, totals, payments, footer, customer), `ReceiptDocument`, `ReprintRequest`, `PrinterProfile` (kind, name/host/port, paper width, copies) |
| Application | `IPrintDocumentService` (`BuildKotAsync`, `BuildInvoiceAsync`, `BuildReceiptAsync`, `RecordReprintAsync`) — composes documents from entities + settings |
| Api | `PrintController` |
| Desktop | `IPrintService`, `PrintJob`, `PrintQueue` (background, retries, failure toast with **Retry**), `EscPosRenderer` (58/80 mm, bold/large fonts, cut), `FlowDocumentRenderer`, `RawPrinterChannel` (winspool `WritePrinter`), `NetworkPrinterChannel`, `WindowsPrinterChannel`, `PrinterSettingsView/VM` (profiles, test print), hooks: kitchen auto-print on `KitchenTicketCreated` (own station), cashier auto-print on finalize/settle, **REPRINT** on Closed Bills and Completed tickets, **PRINT PREVIEW** |
| Tests | see § 10 |

## 5. Data model

No new tables. Reprints recorded in `AuditLogs` (`Print.Reprint` with document type, entity id,
reason). Optional `Settings` keys: `PrintInvoiceCopies`, `KotShowPrices` (false), `ReceiptFooter`.

## 6. API endpoints

`docs/03-api-reference.md` § 3.8.

## 7. Real-time events

None new. Auto-print on the kitchen device is triggered by the existing `KitchenTicketCreated`
event, followed by `GET /api/print/kot/{ticketId}` (source of truth, never print from event payload).

## 8. Business rules

- Documents are built on the server from persisted data; the client never composes invoice content.
- KOT shows table, ticket number, waiter, time, items with qty, notes, modifiers, "+ADD" for later
  batches; never prices.
- Invoice shows restaurant header (name, address, GSTIN, phone), invoice number/date, table, waiter,
  lines, subtotal, discount, tax breakup by rate (split as configured), round-off, grand total,
  payments, customer details when present, footer. Voided bills print with a "CANCELLED" banner.
- Receipt shows invoice number, amount, method, reference, change, time.
- Reprint of invoices/receipts requires Cashier/Manager and is audited with reason; KOT reprint is
  free for Kitchen/Waiter.
- Print failures: logged, toast with **Retry**, job kept in the queue for 10 minutes; business
  operations proceed regardless.
- A device with no printer profile configured for a document type simply skips auto-print.

## 9. Desktop screens

| Screen | Behaviour |
|---|---|
| Printer Settings (device) | Profiles for Kitchen / Invoice / Receipt: kind (RAW / Network / Windows), printer name or host:port, paper width, copies, auto-print toggles; **TEST PRINT** |
| Print preview | FlowDocument viewer for Windows printers; **PRINT** / **CLOSE** |
| Closed Bills / Completed tickets | **REPRINT** (reason dialog for invoices/receipts) |
| Status bar | Print queue indicator with failed count; click to retry/discard |

## 10. Tests

- `EscPosRenderer`: snapshot tests of byte output for KOT/invoice/receipt documents (init, alignment,
  bold, double-height, cut sequences); 58 vs 80 mm column widths; long item names wrap.
- `FlowDocumentRenderer`: produces a document with expected blocks (headless test).
- `PrintQueue`: retries 3 times with a failing channel, raises failure notification, succeeds on
  manual retry; a failing job does not block the next job.
- Application: `BuildInvoiceAsync` tax breakup grouping and CGST/SGST split; voided bill banner.
- Api: Kitchen cannot fetch invoice document (403); reprint creates audit.
- Desktop: auto-print triggers only for the device's station and only when enabled.

## 11. Manual demo script

1. Configure KITCHEN-01 with a thermal printer (RAW) and BILLING-01 with a receipt printer (Network).
   Test print on both.
2. Send an order: KOT prints at the kitchen within 2 s of the ticket appearing on screen.
3. Finalize a bill: invoice prints; settle with cash: receipt prints with change.
4. Power off the receipt printer, settle another bill: payment succeeds, toast reports print failure;
   power on, press Retry: receipt prints.
5. Reprint an invoice from Closed Bills with reason; check audit log.
6. Print preview on a Windows printer (PDF printer) for an invoice.

## 12. Acceptance criteria

- [x] KOT, invoice and receipt print on all three channel kinds from the right device (channels built and unit-tested; the hardware run with real printers, demo steps 1-4 and 6, is still to do by hand).
- [x] Printing is isolated behind `IPrintService`; no business code references printers.
- [x] Print failure never blocks or rolls back a business operation; retry available.
- [x] Documents come from the API; reprints audited.
- [x] Definition of Done satisfied.

## 13. Risks and notes

- ESC/POS dialects differ (code pages, cut commands); keep command tables per printer model in the
  profile (`Model: Generic | Epson | Xprinter`) with a generic default.
- Non-ASCII characters (₹) on thermal printers need a code page or should be replaced by "Rs." via a
  profile setting (`CurrencyFallback`).
- Windows RAW printing requires the printer driver to be "Generic / Text Only" for most thermal
  models; document this in the installation guide.

## 14. Changes during implementation

- **One layout, two outputs.** `EscPosRenderer.Layout` turns a document into fixed-width lines (32 columns for
  58 mm, 48 for 80 mm) with a style per line; `Encode` turns those lines into ESC/POS bytes and
  `FlowDocumentRenderer` typesets the same lines for Windows printers and the on-screen preview, so the preview
  always matches the paper.
- **Printer profiles** are keyed by document type (`Kot`, `Invoice`, `Receipt`) in `settings.json`, so the
  example in docs/05 uses `Kot` instead of `Kitchen`. `AutoPrintReceipt` joins the two auto-print flags from
  the spec. The profile also carries `Model` (cut command) and `CurrencyFallback` (`₹` → `Rs.` on thermal
  printers); everything outside ASCII is mapped to a close character so code pages never garble a ticket.
- **Copies.** The profile's copy count is the default; for invoices the server's `PrintInvoiceCopies` wins when
  it is larger.
- **The print queue** runs one job at a time with retries after 1 s, 3 s and 5 s. A job that still fails is
  kept for 10 minutes, shown as an error toast with a **Retry** button and counted in the status bar (Retry /
  Discard). Business operations never wait for it.
- **Windows printing** happens on a private STA thread (XPS writer to the chosen `PrintQueue`), so neither the
  UI nor the background queue blocks. RAW printing goes through winspool `WritePrinter` with the `RAW` data
  type; network printing opens TCP 9100 with a 5 s timeout.
- **KOT auto-print** is a shell-level service (`KotAutoPrinter`), not part of the Kitchen Display, so a kitchen
  terminal prints even while another screen is open. It fires only on `DeviceType = Kitchen` terminals with
  `AutoPrintKot` and a kitchen printer, and only for the terminal's station (all stations when none is set).
- **Reprints.** Invoice and receipt reprints (Closed Bills) ask for a reason and call `POST /api/print/reprints`
  before printing; KOT reprints (Completed Orders) are free. The Bill Detail **PRINT** button prints the current
  bill without an audit entry (it is the first copy on terminals without auto-print).
- The Printers screen lives under Administration (Admin/Manager) and lists the installed Windows printers.
- Server settings added: `PrintInvoiceCopies` (1) and `KotShowPrices` (false).
