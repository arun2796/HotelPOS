using HotelPOS.Contracts.Enums;

namespace HotelPOS.Contracts.Realtime;

public static class ShiftChangeTypes
{
    public const string Opened = "Opened";
    public const string MovementAdded = "MovementAdded";
    public const string Closed = "Closed";
    public const string HandedOver = "HandedOver";
    public const string ForceClosed = "ForceClosed";
    public const string Reopened = "Reopened";
}

public sealed record ShiftChangedEvent : RealtimeEvent
{
    public int ShiftId { get; init; }
    public Guid DeviceId { get; init; }
    public string DeviceName { get; init; } = string.Empty;
    public string CashierName { get; init; } = string.Empty;
    public DateOnly BusinessDay { get; init; }
    public CashShiftStatus Status { get; init; }
    public string ChangeType { get; init; } = string.Empty;
}

// EntityId is the business day's DayNumber; clients use BusinessDay.
public sealed record BusinessDayChangedEvent : RealtimeEvent
{
    public DateOnly BusinessDay { get; init; }
    public BusinessDayStatus Status { get; init; }
    public string ChangedBy { get; init; } = string.Empty;
}
