using HotelPOS.Contracts.Enums;

namespace HotelPOS.Contracts.Realtime;

public sealed record TableStatusChangedEvent : RealtimeEvent
{
    public int TableId { get; init; }
    public string TableCode { get; init; } = string.Empty;
    public TableStatus Status { get; init; }
    public int? GuestCount { get; init; }
    public DateTime? OccupiedAtUtc { get; init; }
    public int? OrderId { get; init; }
    public int? OrderNumber { get; init; }
}
