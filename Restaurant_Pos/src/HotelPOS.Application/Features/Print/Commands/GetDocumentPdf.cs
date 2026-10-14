using System.Globalization;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Print;

namespace HotelPOS.Application.Features.Print.Commands;

// The invoice or receipt as a PDF: the same render model and the same copy rule as a printed copy (the PDF takes
// the next copy number and is logged with output PDF). A command although it answers a GET, because it writes the
// print log. Paper: A4, A5 or 80mm; none uses the template's sheet size, or PdfPaper for thermal templates.
public sealed record GetDocumentPdfCommand(PrintDocumentType DocumentType, int DocumentId, string? Paper)
    : ICommand<ExportFile>, IAuditedCommand;

internal sealed class GetDocumentPdfHandler : ICommandHandler<GetDocumentPdfCommand, ExportFile>
{
    private readonly IAppDbContext _db;
    private readonly PrintLog _log;
    private readonly IPdfRenderer _pdf;

    public GetDocumentPdfHandler(IAppDbContext db, PrintLog log, IPdfRenderer pdf)
    {
        _db = db;
        _log = log;
        _pdf = pdf;
    }

    public static PaperSize? ParsePaper(string? paper) => paper?.Trim().ToLowerInvariant() switch
    {
        "a4" => PaperSize.A4,
        "a5" => PaperSize.A5,
        "80mm" or "80" or "roll" => PaperSize.Mm80,
        "58mm" or "58" => PaperSize.Mm58,
        _ => null,
    };

    public async Task<Result<ExportFile>> Handle(GetDocumentPdfCommand command, CancellationToken cancellationToken)
    {
        if (command.DocumentType == PrintDocumentType.Kot)
        {
            return AppErrors.Validation("documentType", "Kitchen tickets have no PDF.");
        }

        PaperSize? requested = null;
        if (!string.IsNullOrWhiteSpace(command.Paper))
        {
            requested = ParsePaper(command.Paper);
            if (requested is null)
            {
                return new AppError(ErrorCodes.ValidationError, "Paper must be A4, A5, 80mm or 58mm.")
                {
                    Details = new[] { new ApiError(ErrorCodes.ValidationError, "Paper must be A4, A5, 80mm or 58mm.", "paper") },
                };
            }
        }

        var reserved = await _log.ReserveAsync(command.DocumentType, command.DocumentId, PrintOutput.Pdf, null, null, cancellationToken);
        if (reserved.IsFailure)
        {
            return reserved.Error!;
        }

        var fallback = ParsePaper(await _db.SettingAsync(SettingKeys.PdfPaper, cancellationToken)) ?? PaperSize.A4;
        var copy = reserved.Value.Entry.CopyNumber.ToString(CultureInfo.InvariantCulture);
        switch (reserved.Value.Document)
        {
            case InvoiceDocument invoice:
                var invoicePaper = requested ?? SheetOr(invoice.Template, fallback);
                var name = Safe(invoice.InvoiceNumber ?? "bill-" + invoice.BillNumber.ToString(CultureInfo.InvariantCulture));
                return new ExportFile(_pdf.Invoice(invoice, invoicePaper), ExportFormats.ContentType(ExportFormat.Pdf), $"invoice-{name}-copy{copy}.pdf");
            case ReceiptDocument receipt:
                var receiptPaper = requested ?? SheetOr(receipt.Template, fallback);
                return new ExportFile(_pdf.Receipt(receipt, receiptPaper), ExportFormats.ContentType(ExportFormat.Pdf),
                    $"receipt-{receipt.PaymentId.ToString(CultureInfo.InvariantCulture)}-copy{copy}.pdf");
            default:
                return AppErrors.Validation("documentType", "This document has no PDF.");
        }
    }

    private static PaperSize SheetOr(TemplateLayout? template, PaperSize fallback) =>
        template is { Paper: PaperSize.A4 or PaperSize.A5 } ? template.Paper : fallback;

    private static string Safe(string text) => new(text.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '-').ToArray());
}
