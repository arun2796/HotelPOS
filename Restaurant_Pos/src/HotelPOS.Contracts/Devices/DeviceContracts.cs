using HotelPOS.Contracts.Enums;

namespace HotelPOS.Contracts.Devices;

public sealed record RegisterDeviceRequest
{
    public string Name { get; init; } = string.Empty;
    public DeviceType Type { get; init; }
    public string? MachineName { get; init; }
    public string? AppVersion { get; init; }
    public int? StationId { get; init; }
}

public sealed record DeviceDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public DeviceType Type { get; init; }
    public string? MachineName { get; init; }
    public string? AppVersion { get; init; }
    public int? StationId { get; init; }
    public bool IsActive { get; init; }
    public bool IsOnline { get; init; }
    public DateTime? LastSeenAtUtc { get; init; }
    public DateTime RegisteredAtUtc { get; init; }
}

public static class DeviceNameRules
{
    public const int MinLength = 2;
    public const int MaxLength = 50;

    /// <summary>Letters, digits, dash and underscore, e.g. WAITER-01.</summary>
    public const string Pattern = "^[A-Za-z0-9_-]+$";
}
