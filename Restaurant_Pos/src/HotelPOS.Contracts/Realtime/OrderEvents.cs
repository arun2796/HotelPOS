using HotelPOS.Contracts.Enums;

namespace HotelPOS.Contracts.Realtime;

public static class OrderChangeTypes
{
    public const string ItemsAppended = "ItemsAppended";
    public const string GuestsChanged = "GuestsChanged";
    public const string ItemCancelled = "ItemCancelled";
    public const string StatusDerived = "StatusDerived";
    public const string BillRequested = "BillRequested";
    public const string Billed = "Billed";
    public const string Paid = "Paid";
    public const string Completed = "Completed";
    public const string BillReopened = "BillReopened";
    public const string BillVoided = "BillVoided";
}

public sealed record OrderCreatedEvent : RealtimeEvent
{
    public int OrderId { get; init; }
    public int OrderNumber { get; init; }
    public string TableCode { get; init; } = string.Empty;
    public string WaiterName { get; init; } = string.Empty;
    public int ItemCount { get; init; }
}

public sealed record OrderUpdatedEvent : RealtimeEvent
{
    public int OrderId { get; init; }
    public int OrderNumber { get; init; }
    public OrderStatus Status { get; init; }
    public string ChangeType { get; init; } = string.Empty;
}

public sealed record OrderCancelledEvent : RealtimeEvent
{
    public int OrderId { get; init; }
    public int OrderNumber { get; init; }
    public string TableCode { get; init; } = string.Empty;
    public string? Reason { get; init; }
}
