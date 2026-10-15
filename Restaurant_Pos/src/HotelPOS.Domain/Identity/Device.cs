using HotelPOS.Contracts.Enums;
using HotelPOS.Domain.Common;

namespace HotelPOS.Domain.Identity;

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
    public DateTime? DeactivatedAt { get; private set; }

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

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Device name is required.");
        }

        Name = Normalize(name);
    }

    public void AssignStation(int? stationId) => StationId = stationId;

    public void Touch(DateTime nowUtc) => LastSeenAt = nowUtc;

    public void Activate()
    {
        IsActive = true;
        DeactivatedAt = null;
    }

    public void Deactivate(DateTime? deactivatedAt = null)
    {
        IsActive = false;
        DeactivatedAt = deactivatedAt ?? DateTime.UtcNow;
    }

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length <= max ? value.Trim() : value.Trim()[..max];
}
