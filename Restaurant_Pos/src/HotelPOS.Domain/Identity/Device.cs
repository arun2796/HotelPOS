using HotelPOS.Contracts.Enums;
using HotelPOS.Domain.Common;

namespace HotelPOS.Domain.Identity;

/// <summary>A registered client installation (e.g. WAITER-01), independent of who is logged in.</summary>
public sealed class Device : IAuditable
{
    private Device()
    {
    }

    public Device(Guid id, string name, DeviceType type, DateTime registeredAtUtc)
    {
        Id = id;
        Name = Normalize(name);
        Type = type;
        RegisteredAt = registeredAtUtc;
        IsActive = true;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public DeviceType Type { get; private set; }
    public string? MachineName { get; private set; }
    public string? AppVersion { get; private set; }
    public int? StationId { get; private set; }
    public DateTime? LastSeenAt { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime RegisteredAt { get; private set; }

    public DateTime CreatedAt { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedBy { get; set; }

    public static string Normalize(string name) => name.Trim().ToUpperInvariant();

    public void UpdateRegistration(DeviceType type, string? machineName, string? appVersion, int? stationId)
    {
        Type = type;
        MachineName = Truncate(machineName, 100);
        AppVersion = Truncate(appVersion, 30);
        StationId = stationId;
    }

    public void Touch(DateTime nowUtc) => LastSeenAt = nowUtc;

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length <= max ? value.Trim() : value.Trim()[..max];
}
