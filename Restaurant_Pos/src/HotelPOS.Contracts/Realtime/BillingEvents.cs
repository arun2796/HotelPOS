using HotelPOS.Contracts.Enums;

namespace HotelPOS.Contracts.Realtime;

public static class BillChangeTypes
{
    public const string Claimed = "Claimed";
    public const string Released = "Released";
    public const string DiscountChanged = "DiscountChanged";
    public const string CustomerChanged = "CustomerChanged";
    public const string Finalized = "Finalized";
    public const string PaymentReceived = "PaymentReceived";
    public const string Reopened = "Reopened";
    public const string Voided = "Voided";
    public const string Refunded = "Refunded";
}

public sealed record BillRequestedEvent : RealtimeEvent
{
    public int BillId { get; init; }
    public int BillNumber { get; init; }
    public int OrderId { get; init; }
    public int OrderNumber { get; init; }
    public string TableCode { get; init; } = string.Empty;
    public string WaiterName { get; init; } = string.Empty;
    public decimal GrandTotal { get; init; }
}

public sealed record BillUpdatedEvent : RealtimeEvent
{
    public int BillId { get; init; }
    public int OrderId { get; init; }
    public string TableCode { get; init; } = string.Empty;
    public BillStatus Status { get; init; }
    public PaymentStatus PaymentStatus { get; init; }
    public string? ClaimedByDevice { get; init; }
    public decimal GrandTotal { get; init; }
    public decimal PaidAmount { get; init; }
    public string ChangeType { get; init; } = string.Empty;
}

public sealed record PaymentCompletedEvent : RealtimeEvent
{
    public int BillId { get; init; }
    public int OrderId { get; init; }
    public int TableId { get; init; }
    public string TableCode { get; init; } = string.Empty;
    public string? InvoiceNumber { get; init; }
    public decimal GrandTotal { get; init; }
}
