namespace HotelPOS.Contracts.Realtime;

/// <summary>
/// The menu changed (any category, item, modifier, tax or station write, or an availability toggle).
/// Clients holding an older <see cref="MenuVersion"/> reload <c>GET /api/menu</c>.
/// </summary>
public sealed record MenuChangedEvent : RealtimeEvent
{
    public int MenuVersion { get; init; }
}
