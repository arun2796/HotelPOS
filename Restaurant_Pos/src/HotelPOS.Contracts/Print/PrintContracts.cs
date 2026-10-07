namespace HotelPOS.Contracts.Print;

public enum PrintDocumentType
{
    Kot = 1,
    Invoice = 2,
    Receipt = 3,
}

public enum PrinterKind
{
    EscPosRaw = 1,
    Network = 2,
    Windows = 3,
}

public enum PrinterModel
{
    Generic = 1,
    Epson = 2,
    Xprinter = 3,
}

public static class PrintLimits
{
    public const int ReasonMaxLength = 300;
    public const int MaxCopies = 5;
    public const int DefaultPort = 9100;
}

// Lives in the terminal's settings.json; the server never sees it.
public sealed record PrinterProfile
{
    public PrinterKind Kind { get; init; } = PrinterKind.EscPosRaw;
    public string PrinterName { get; init; } = string.Empty;
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = PrintLimits.DefaultPort;
    public int PaperWidthMm { get; init; } = 80;
    public int Copies { get; init; } = 1;
    public PrinterModel Model { get; init; } = PrinterModel.Generic;
    public string? CurrencyFallback { get; init; } = "Rs.";

    public string Target => Kind switch
    {
        PrinterKind.Network => $"{Host}:{Port}",
        _ => PrinterName,
    };
}

public sealed record KotLine
{
    public string Name { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public string? Notes { get; init; }
    public IReadOnlyList<string> Modifiers { get; init; } = Array.Empty<string>();
    public bool IsCancelled { get; init; }
    public decimal? UnitPrice { get; init; }
}

public sealed record KitchenTicketDocument
{
    public int TicketId { get; init; }
    public string TicketNumber { get; init; } = string.Empty;
    public int OrderNumber { get; init; }
    public int BatchNumber { get; init; }
    public bool IsAddition { get; init; }
    public string TableCode { get; init; } = string.Empty;
    public string WaiterName { get; init; } = string.Empty;
    public string StationCode { get; init; } = string.Empty;
    public string StationName { get; init; } = string.Empty;
    public int GuestCount { get; init; }
    public string? OrderNotes { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime PrintedAtUtc { get; init; }
    public bool ShowPrices { get; init; }
    public string CurrencySymbol { get; init; } = string.Empty;
    public IReadOnlyList<KotLine> Items { get; init; } = Array.Empty<KotLine>();
}

public sealed record InvoiceLine
{
    public string Name { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal LineSubtotal { get; init; }
    public decimal TaxRatePercent { get; init; }
}

public sealed record TaxRow
{
    public string Label { get; init; } = string.Empty;
    public decimal RatePercent { get; init; }
    public decimal TaxableAmount { get; init; }
    public decimal TaxAmount { get; init; }
}

public sealed record InvoicePaymentLine
{
    public string Method { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string? Reference { get; init; }
    public bool IsRefund { get; init; }
}

public sealed record InvoiceCustomer
{
    public string? Name { get; init; }
    public string? Phone { get; init; }
    public string? Gstin { get; init; }
}

public sealed record InvoiceDocument
{
    public int BillId { get; init; }
    public int BillNumber { get; init; }
    public string? InvoiceNumber { get; init; }
    public DateTime IssuedAtUtc { get; init; }
    public DateTime PrintedAtUtc { get; init; }
    public string RestaurantName { get; init; } = string.Empty;
    public string? Address { get; init; }
    public string? Phone { get; init; }
    public string? Gstin { get; init; }
    public string CurrencySymbol { get; init; } = string.Empty;
    public string TableCode { get; init; } = string.Empty;
    public int OrderNumber { get; init; }
    public string WaiterName { get; init; } = string.Empty;
    public int GuestCount { get; init; }
    public InvoiceCustomer? Customer { get; init; }
    public IReadOnlyList<InvoiceLine> Lines { get; init; } = Array.Empty<InvoiceLine>();
    public decimal Subtotal { get; init; }
    public string? DiscountLabel { get; init; }
    public decimal DiscountAmount { get; init; }
    public decimal TaxableAmount { get; init; }
    public IReadOnlyList<TaxRow> TaxRows { get; init; } = Array.Empty<TaxRow>();
    public decimal TaxAmount { get; init; }
    public decimal RoundOff { get; init; }
    public decimal GrandTotal { get; init; }
    public IReadOnlyList<InvoicePaymentLine> Payments { get; init; } = Array.Empty<InvoicePaymentLine>();
    public decimal PaidAmount { get; init; }
    public decimal BalanceDue { get; init; }
    public bool IsCancelled { get; init; }
    public string? CancelReason { get; init; }
    public string? Footer { get; init; }
    public int Copies { get; init; } = 1;
}

public sealed record ReceiptDocument
{
    public int PaymentId { get; init; }
    public int BillId { get; init; }
    public string? InvoiceNumber { get; init; }
    public string RestaurantName { get; init; } = string.Empty;
    public string CurrencySymbol { get; init; } = string.Empty;
    public string TableCode { get; init; } = string.Empty;
    public int OrderNumber { get; init; }
    public string Method { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public decimal? TenderedAmount { get; init; }
    public decimal? ChangeAmount { get; init; }
    public string? Reference { get; init; }
    public bool IsRefund { get; init; }
    public string? RefundReason { get; init; }
    public DateTime PaidAtUtc { get; init; }
    public DateTime PrintedAtUtc { get; init; }
    public string ReceivedBy { get; init; } = string.Empty;
    public decimal GrandTotal { get; init; }
    public decimal PaidAmount { get; init; }
    public decimal BalanceDue { get; init; }
    public string? Footer { get; init; }
}

public sealed record ReprintRequest
{
    public PrintDocumentType DocumentType { get; init; }
    public int EntityId { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public sealed record TestPrintDocument
{
    public string DeviceName { get; init; } = string.Empty;
    public string ProfileName { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public DateTime PrintedAtUtc { get; init; }
}
