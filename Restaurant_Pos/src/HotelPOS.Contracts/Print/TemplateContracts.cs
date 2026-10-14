namespace HotelPOS.Contracts.Print;

public enum PaperSize
{
    Mm58 = 1,
    Mm80 = 2,
    A5 = 3,
    A4 = 4,
}

// The columns of the invoice item table, in the order the template lists them.
public enum TemplateColumn
{
    Item = 1,
    HsnSac = 2,
    Quantity = 3,
    Rate = 4,
    Discount = 5,
    TaxPercent = 6,
    Amount = 7,
}

public enum GstBreakupStyle
{
    // CGST/SGST rows or one GST row per rate, as the TaxSplitDisplay setting says (the Phase 7 output).
    FollowSetting = 0,
    SplitRows = 1,
    RateTable = 2,
    SingleLine = 3,
}

public enum TemplateFontSize
{
    Normal = 1,
    Large = 2,
}

public enum PrintOutput
{
    Printer = 1,
    Pdf = 2,
    Email = 3,
}

public enum PrintResult
{
    Reserved = 1,
    Printed = 2,
    Failed = 3,
}

public static class TemplateLimits
{
    public const int NameMaxLength = 80;
    public const int MaxLines = 6;
    public const int LineMaxLength = 120;
    public const int PrinterNameMaxLength = 100;
    public const int ErrorMaxLength = 500;
    public const int LogoMaxBytes = 2 * 1024 * 1024;
    public const int LogoMaxPixels = 600;
    public const string DuplicateLabel = "DUPLICATE";
}

// How a document is laid out. Presentation only: amounts, tax and numbers always come from the persisted bill.
// Stored as JSON in DocumentTemplates.LayoutJson; one version per document type is active.
public sealed record TemplateLayout
{
    public PaperSize Paper { get; init; } = PaperSize.Mm80;
    public bool ShowLogo { get; init; }

    // Extra lines under the restaurant header (licence numbers, a tagline) and above the closing lines.
    public IReadOnlyList<string> HeaderLines { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> FooterLines { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Terms { get; init; } = Array.Empty<string>();

    public IReadOnlyList<TemplateColumn> Columns { get; init; } = DefaultColumns;
    public GstBreakupStyle GstBreakup { get; init; } = GstBreakupStyle.FollowSetting;
    public bool ShowUpiQr { get; init; }
    public TemplateFontSize FontSize { get; init; } = TemplateFontSize.Normal;

    public bool ShowAddress { get; init; } = true;
    public bool ShowPhone { get; init; } = true;
    public bool ShowGstin { get; init; } = true;
    public bool ShowWaiter { get; init; } = true;
    public bool ShowGuests { get; init; } = true;
    public bool ShowCustomer { get; init; } = true;
    public bool ShowPayments { get; init; } = true;
    public bool ShowPrintedAt { get; init; } = true;

    // Kitchen tickets. KotShowPrices null follows the KotShowPrices setting (the Phase 7 behaviour).
    public bool? KotShowPrices { get; init; }
    public bool KotNotesBold { get; init; } = true;
    public bool KotStationHeading { get; init; }

    public static IReadOnlyList<TemplateColumn> DefaultColumns { get; } =
        new[] { TemplateColumn.Item, TemplateColumn.Quantity, TemplateColumn.Rate, TemplateColumn.Amount };

    // The seeded version 1 of every document type: exactly the Phase 7 output.
    public static TemplateLayout DefaultFor(PrintDocumentType type) => new();

    public static bool IsThermal(PaperSize paper) => paper is PaperSize.Mm58 or PaperSize.Mm80;
}

public sealed record DocumentTemplateDto
{
    public int Id { get; init; }
    public PrintDocumentType DocumentType { get; init; }
    public int Version { get; init; }
    public string Name { get; init; } = string.Empty;
    public TemplateLayout Layout { get; init; } = new();
    public bool IsActive { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public string? CreatedBy { get; init; }
    public DateTime? ActivatedAtUtc { get; init; }
    public string? ActivatedBy { get; init; }
}

public sealed record SaveTemplateRequest
{
    public string Name { get; init; } = string.Empty;
    public TemplateLayout Layout { get; init; } = new();

    // Activate the new version at once (otherwise it is saved as a draft version).
    public bool Activate { get; init; }
}

public sealed record TemplatePreviewRequest
{
    public PrintDocumentType DocumentType { get; init; }
    public TemplateLayout Layout { get; init; } = new();

    // A real bill (invoice), payment (receipt) or ticket (KOT); none renders sample data.
    public int? SourceId { get; init; }
}

// The render model the layout produces; exactly one document is set.
public sealed record TemplatePreviewDto
{
    public PrintDocumentType DocumentType { get; init; }
    public bool IsSample { get; init; }
    public KitchenTicketDocument? Kot { get; init; }
    public InvoiceDocument? Invoice { get; init; }
    public ReceiptDocument? Receipt { get; init; }
}

public sealed record TemplateActivatedEvent : Realtime.RealtimeEvent
{
    public PrintDocumentType DocumentType { get; init; }
    public int Version { get; init; }
}

// Reserve the next copy of a document before printing it (Idempotency-Key header required).
public sealed record ReserveCopyRequest
{
    public PrintOutput Output { get; init; } = PrintOutput.Printer;
    public string? PrinterName { get; init; }
}

public sealed record CompletePrintRequest
{
    public bool Succeeded { get; init; }
    public string? Error { get; init; }
}

public sealed record PrintLogDto
{
    public long Id { get; init; }
    public PrintDocumentType DocumentType { get; init; }
    public int DocumentId { get; init; }
    public int CopyNumber { get; init; }
    public string? CopyLabel { get; init; }
    public PrintOutput Output { get; init; }
    public string? PrinterName { get; init; }
    public string? DeviceName { get; init; }
    public string? UserName { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public PrintResult Result { get; init; }
    public string? Error { get; init; }
}

public sealed record DocumentPrintedEvent : Realtime.RealtimeEvent
{
    public PrintDocumentType DocumentType { get; init; }
    public int DocumentId { get; init; }
    public int CopyNumber { get; init; }
    public PrintOutput Output { get; init; }
    public PrintResult Result { get; init; }
}

public static class PrintCopies
{
    // Copy 1 is the original; every later copy says DUPLICATE. 0 is a preview (no copy reserved).
    public static string? LabelFor(int copyNumber) => copyNumber > 1 ? TemplateLimits.DuplicateLabel : null;
}
