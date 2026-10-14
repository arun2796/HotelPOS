using HotelPOS.Contracts.Reports;

namespace HotelPOS.Contracts.Gst;

// How an outward supply is reported in GSTR-1: to a registered buyer (B2B), a large inter-state supply to an
// unregistered buyer (B2CL), or everything else to unregistered buyers (B2CS, summarised).
public enum GstSupplyType
{
    B2B = 0,
    B2CL = 1,
    B2CS = 2,
}

public static class GstLimits
{
    public const int LegalNameMaxLength = 200;
    public const int StateCodeLength = 2;
    public const int HsnMaxLength = 8;
}

// A GST state or union territory (code as in the first two digits of a GSTIN).
public sealed record GstStateDto(string Code, string Name);

public sealed record GstPeriodQuery
{
    // yyyy-MM; the previous calendar month when omitted.
    public string? Period { get; init; }

    public string? Format { get; init; }
}

public sealed record Gstr1InvoiceRowDto
{
    public string InvoiceNumber { get; init; } = string.Empty;
    public DateOnly InvoiceDate { get; init; }
    public string CustomerGstin { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    public string PlaceOfSupply { get; init; } = string.Empty;
    public decimal InvoiceValue { get; init; }
    public decimal TaxableValue { get; init; }
    public decimal Igst { get; init; }
    public decimal Cgst { get; init; }
    public decimal Sgst { get; init; }
}

public sealed record Gstr1B2csRowDto
{
    public string SupplyType { get; init; } = string.Empty;
    public string PlaceOfSupply { get; init; } = string.Empty;

    [NoTotal]
    public decimal Rate { get; init; }

    public decimal TaxableValue { get; init; }
    public decimal Igst { get; init; }
    public decimal Cgst { get; init; }
    public decimal Sgst { get; init; }
}

public sealed record Gstr1NoteRowDto
{
    public string Section { get; init; } = string.Empty;
    public string NoteNumber { get; init; } = string.Empty;
    public DateOnly NoteDate { get; init; }
    public string InvoiceNumber { get; init; } = string.Empty;
    public string CustomerGstin { get; init; } = string.Empty;
    public string PlaceOfSupply { get; init; } = string.Empty;
    public decimal NoteValue { get; init; }
    public decimal TaxableValue { get; init; }
    public decimal Igst { get; init; }
    public decimal Cgst { get; init; }
    public decimal Sgst { get; init; }
}

public sealed record HsnSummaryRowDto
{
    public string HsnSac { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;

    [NoTotal]
    public decimal Rate { get; init; }

    public int Quantity { get; init; }
    public decimal TotalValue { get; init; }
    public decimal TaxableValue { get; init; }
    public decimal Igst { get; init; }
    public decimal Cgst { get; init; }
    public decimal Sgst { get; init; }
}

public sealed record Gstr1DocumentRowDto
{
    public string Document { get; init; } = string.Empty;
    public string From { get; init; } = string.Empty;
    public string To { get; init; } = string.Empty;
    public int TotalIssued { get; init; }
    public int Cancelled { get; init; }
    public int NetIssued { get; init; }
}

public sealed record GstTotalsDto
{
    public int Invoices { get; init; }
    public decimal InvoiceValue { get; init; }
    public decimal TaxableValue { get; init; }
    public decimal Igst { get; init; }
    public decimal Cgst { get; init; }
    public decimal Sgst { get; init; }
    public decimal CreditNoteTaxable { get; init; }
    public decimal CreditNoteTax { get; init; }
}

public sealed record Gstr1Dto
{
    public string Period { get; init; } = string.Empty;
    public string FilingPeriod { get; init; } = string.Empty;
    public string Gstin { get; init; } = string.Empty;
    public decimal B2clThreshold { get; init; }
    public GstTotalsDto Totals { get; init; } = new();
    public IReadOnlyList<Gstr1InvoiceRowDto> B2b { get; init; } = Array.Empty<Gstr1InvoiceRowDto>();
    public IReadOnlyList<Gstr1InvoiceRowDto> B2cl { get; init; } = Array.Empty<Gstr1InvoiceRowDto>();
    public IReadOnlyList<Gstr1B2csRowDto> B2cs { get; init; } = Array.Empty<Gstr1B2csRowDto>();
    public IReadOnlyList<Gstr1NoteRowDto> CreditNotes { get; init; } = Array.Empty<Gstr1NoteRowDto>();
    public IReadOnlyList<HsnSummaryRowDto> Hsn { get; init; } = Array.Empty<HsnSummaryRowDto>();
    public IReadOnlyList<Gstr1DocumentRowDto> Documents { get; init; } = Array.Empty<Gstr1DocumentRowDto>();
    public int IssueCount { get; init; }
    public int ErrorCount { get; init; }
}

// GSTR-3B table 3.1 (a) outward taxable supplies and (c) other outward supplies (nil rated, exempted), net of credit
// notes, and 3.2 inter-state supplies to unregistered buyers by place of supply.
public sealed record Gstr3bTableRowDto
{
    public string Table { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    [NoTotal]
    public decimal TaxableValue { get; init; }
    [NoTotal]
    public decimal Igst { get; init; }
    [NoTotal]
    public decimal Cgst { get; init; }
    [NoTotal]
    public decimal Sgst { get; init; }
    [NoTotal]
    public decimal Cess { get; init; }
}

public sealed record Gstr3bDto
{
    public string Period { get; init; } = string.Empty;
    public string FilingPeriod { get; init; } = string.Empty;
    public string Gstin { get; init; } = string.Empty;
    public IReadOnlyList<Gstr3bTableRowDto> Rows { get; init; } = Array.Empty<Gstr3bTableRowDto>();
}

public static class GstIssueSeverity
{
    public const string Error = "Error";
    public const string Warning = "Warning";
}

public sealed record GstValidationRowDto
{
    public string Severity { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string Reference { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public static class GstIssueCodes
{
    public const string RestaurantGstinMissing = "RESTAURANT_GSTIN_MISSING";
    public const string RestaurantGstinInvalid = "RESTAURANT_GSTIN_INVALID";
    public const string CustomerGstinInvalid = "CUSTOMER_GSTIN_INVALID";
    public const string CustomerStateMismatch = "CUSTOMER_STATE_MISMATCH";
    public const string InvoiceNumberMissing = "INVOICE_NUMBER_MISSING";
    public const string SalesTotalMismatch = "SALES_TOTAL_MISMATCH";
    public const string SalesTaxMismatch = "SALES_TAX_MISMATCH";
    public const string PendingBills = "PENDING_BILLS";
}

public sealed record GstValidationDto
{
    public string Period { get; init; } = string.Empty;
    public bool CanFile { get; init; }
    public IReadOnlyList<GstValidationRowDto> Issues { get; init; } = Array.Empty<GstValidationRowDto>();
}

public enum PeriodKind
{
    Month = 0,
    Year = 1,
}

public enum PeriodStatus
{
    Open = 0,
    Closed = 1,
    Reopened = 2,
}

public sealed record PeriodCheckDto
{
    public string Code { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public bool Passed { get; init; }
    public IReadOnlyList<string> Details { get; init; } = Array.Empty<string>();
}

public static class PeriodCheckCodes
{
    public const string PeriodEnded = "PERIOD_ENDED";
    public const string DaysClosed = "DAYS_CLOSED";
    public const string PendingBills = "PENDING_BILLS";
    public const string MonthsClosed = "MONTHS_CLOSED";
    public const string GstErrors = "GST_ERRORS";
    public const string PreviousYear = "OPEN_MONTHS_BEFORE";
}

public sealed record PeriodPrecheckDto
{
    public PeriodKind Kind { get; init; }
    public string Period { get; init; } = string.Empty;
    public bool CanClose { get; init; }
    public IReadOnlyList<PeriodCheckDto> Checks { get; init; } = Array.Empty<PeriodCheckDto>();
}

public sealed record PeriodDto
{
    public PeriodKind Kind { get; init; }

    // yyyy-MM for a month; the first calendar year of the financial year (yyyy, April to March) for a year.
    public string Period { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }
    public PeriodStatus Status { get; init; }
    public DateTime? ClosedAtUtc { get; init; }
    public string? ClosedBy { get; init; }
    public string? PackFolder { get; init; }
    public DateTime? ReopenedAtUtc { get; init; }
    public string? ReopenedBy { get; init; }
    public string? ReopenReason { get; init; }
}

public sealed record ClosePeriodRequest
{
    public PeriodKind Kind { get; init; }
    public string Period { get; init; } = string.Empty;
}

public sealed record ReopenPeriodRequest
{
    public PeriodKind Kind { get; init; }
    public string Period { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}

public sealed record PeriodCloseResultDto
{
    public PeriodDto Period { get; init; } = new();
    public IReadOnlyList<string> PackFiles { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

// One month of the year-close pack.
public sealed record GstMonthSummaryRowDto
{
    public string Month { get; init; } = string.Empty;
    public int Invoices { get; init; }
    public decimal InvoiceValue { get; init; }
    public decimal TaxableValue { get; init; }
    public decimal Igst { get; init; }
    public decimal Cgst { get; init; }
    public decimal Sgst { get; init; }
    public decimal CreditNoteTaxable { get; init; }
    public decimal CreditNoteTax { get; init; }
}
