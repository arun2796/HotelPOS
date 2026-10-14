namespace HotelPOS.Contracts.Realtime;

public sealed record DeviceStatusChangedEvent
{
    public Guid DeviceId { get; init; }
    public string DeviceName { get; init; } = string.Empty;
    public bool IsOnline { get; init; }
    public bool IsActive { get; init; }
    public DateTime TimestampUtc { get; init; }
}

public sealed record ServerNoticeEvent
{
    public string Message { get; init; } = string.Empty;
    public string? Code { get; init; }
}

