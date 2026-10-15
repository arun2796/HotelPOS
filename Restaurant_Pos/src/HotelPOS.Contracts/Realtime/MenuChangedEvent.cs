namespace HotelPOS.Contracts.Realtime;

public sealed record MenuChangedEvent : RealtimeEvent
{
    public int MenuVersion { get; init; }
}
