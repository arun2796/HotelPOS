using HotelPOS.Contracts.Enums;

namespace HotelPOS.Contracts.Realtime;

/// <summary>
/// A table changed status (occupy, release, out of service, and from Phase 4 every order/bill
/// transition). <see cref="RealtimeEvent.EntityId"/> is the table id and
/// <see cref="RealtimeEvent.EntityVersion"/> its row version. Guest count and occupied time are included so
/// table maps can update a tile without fetching it.
/// </summary>
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
