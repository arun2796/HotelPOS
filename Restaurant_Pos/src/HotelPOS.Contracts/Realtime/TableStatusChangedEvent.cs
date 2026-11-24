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

    // Merge: set on a companion table (locked, billed on the primary table's order).
    public int? MergedIntoTableId { get; init; }
    public string? MergedIntoTableCode { get; init; }

    // Companion table codes of a primary table; a client may also just refresh its map on any table event.
    public IReadOnlyList<string> MergedTableCodes { get; init; } = Array.Empty<string>();
}
