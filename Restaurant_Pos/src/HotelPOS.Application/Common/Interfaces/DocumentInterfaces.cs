using HotelPOS.Contracts.Print;

namespace HotelPOS.Application.Common.Interfaces;

public enum ExportFormat
{
    Json = 0,
    Csv = 1,
    Xlsx = 2,
    Pdf = 3,
}

public static class ExportFormats
{
    // The "format" query value of report and audit endpoints; anything unknown is the JSON envelope.
    public static ExportFormat Parse(string? format) => format?.Trim().ToLowerInvariant() switch
    {
        "csv" => ExportFormat.Csv,
        "xlsx" or "excel" => ExportFormat.Xlsx,
        "pdf" => ExportFormat.Pdf,
        _ => ExportFormat.Json,
    };

    public static string ContentType(ExportFormat format) => format switch
    {
        ExportFormat.Csv => "text/csv; charset=utf-8",
        ExportFormat.Xlsx => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ExportFormat.Pdf => "application/pdf",
        _ => "application/json",
    };

    public static string Extension(ExportFormat format) => format switch
    {
        ExportFormat.Csv => ".csv",
        ExportFormat.Xlsx => ".xlsx",
        ExportFormat.Pdf => ".pdf",
        _ => ".json",
    };
}

public sealed record ExportFile(byte[] Content, string ContentType, string FileName);

// The restaurant's letterhead on report PDFs and Excel title rows.
public sealed record Letterhead
{
    public string RestaurantName { get; init; } = string.Empty;
    public string? Address { get; init; }
    public string? Phone { get; init; }
    public string? Gstin { get; init; }
    public byte[]? LogoPng { get; init; }
}

public enum ReportCellKind
{
    Text,
    Integer,
    Money,
    Decimal,
    Date,
    DateTime,
}

public sealed record ReportTableColumn(string Header, ReportCellKind Kind, bool HasTotal);

// One report grid as typed cells: what the screen, the CSV, the Excel sheet and the PDF all show. Dates are local
// (DateTime with Kind Local or Unspecified, DateOnly), amounts decimal, counts long.
public sealed record ReportTable
{
    public string Title { get; init; } = string.Empty;
    public string? Subtitle { get; init; }
    public string SheetName { get; init; } = "Report";
    public IReadOnlyList<ReportTableColumn> Columns { get; init; } = Array.Empty<ReportTableColumn>();
    public IReadOnlyList<IReadOnlyList<object?>> Rows { get; init; } = Array.Empty<IReadOnlyList<object?>>();

    // Null when no column has a total (or there are no rows); the first cell is the "Total" label.
    public IReadOnlyList<object?>? Totals { get; init; }

    public DateTime GeneratedAtLocal { get; init; }
}

public interface IPdfRenderer
{
    byte[] Invoice(InvoiceDocument document, PaperSize paper);

    byte[] Receipt(ReceiptDocument document, PaperSize paper);

    // The kitchen ticket (Phase 22 P6), printed from a browser; thermal rolls by default.
    byte[] Kitchen(KitchenTicketDocument document, PaperSize paper);

    byte[] Report(ReportTable table, Letterhead letterhead);

    // Several reports in one PDF, one page run each (the Day End pack of Phase 13).
    byte[] Reports(IReadOnlyList<ReportTable> tables, Letterhead letterhead);
}

public interface IExcelExporter
{
    byte[] Report(ReportTable table, Letterhead letterhead);

    // Several reports in one workbook, one sheet each (the Day End pack of Phase 13).
    byte[] Workbook(IReadOnlyList<ReportTable> tables, Letterhead letterhead);
}

// UPI payment QR codes as PNG images.
public interface IQrCodeRenderer
{
    byte[] Png(string payload);
}

// Copy numbers per document, allocated one after another inside the command's transaction.
public interface IPrintCopyAllocator
{
    Task<int> NextCopyNumberAsync(PrintDocumentType documentType, int documentId, CancellationToken cancellationToken = default);
}

// The restaurant logo (Print templates): stored as a PNG beside the menu pictures.
public interface ILogoStore
{
    Task<string?> SaveAsync(Stream content, CancellationToken cancellationToken = default);

    byte[]? Read(string? fileName);

    void Delete(string fileName);
}
