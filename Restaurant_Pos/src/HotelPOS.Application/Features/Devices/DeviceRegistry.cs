using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Contracts.Devices;
using HotelPOS.Contracts.Enums;
using HotelPOS.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Devices;

// Terminals identify themselves by name at login and registration; an unknown name registers a new device.
internal sealed class DeviceRegistry
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly IClock _clock;

    public DeviceRegistry(IAppDbContext db, IAuditTrail audit, IClock clock)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Device> UpsertAsync(
        string name,
        DeviceType type,
        string? machineName,
        string? appVersion,
        int? stationId,
        CancellationToken cancellationToken)
    {
        var normalized = Device.Normalize(name);
        var device = await _db.Devices.FirstOrDefaultAsync(d => d.Name == normalized, cancellationToken);
        if (device is null)
        {
            device = new Device(Guid.NewGuid(), normalized, type, _clock.UtcNow);
            _db.Devices.Add(device);
            _audit.Add(AuditActions.DeviceRegistered, nameof(Device), device.Id.ToString(),
                newValues: new { device.Name, Type = type.ToString(), MachineName = machineName });
        }

        device.UpdateRegistration(type, machineName, appVersion, stationId ?? device.StationId);
        return device;
    }

    // What a terminal gets back when it registers itself.
    public static DeviceDto ToRegistrationDto(Device device) => new()
    {
        Id = device.Id,
        Name = device.Name,
        Type = device.Type,
        MachineName = device.MachineName,
        AppVersion = device.AppVersion,
        StationId = device.StationId,
        IsActive = device.IsActive,
        IsOnline = false,
        LastSeenAtUtc = device.LastSeenAt,
        RegisteredAtUtc = device.RegisteredAt,
    };

    // The admin device list: presence from the hub, station names, deactivation time.
    public static DeviceDto ToAdminDto(Device device, bool isOnline, IReadOnlyDictionary<int, string> stations) => new()
    {
        Id = device.Id,
        Name = device.Name,
        Type = device.Type,
        MachineName = device.MachineName,
        AppVersion = device.AppVersion,
        StationId = device.StationId,
        StationName = device.StationId is { } stationId && stations.TryGetValue(stationId, out var name) ? name : null,
        IsActive = device.IsActive,
        IsOnline = isOnline,
        LastSeenAtUtc = device.LastSeenAt,
        RegisteredAtUtc = device.RegisteredAt,
        DeactivatedAtUtc = device.DeactivatedAt,
    };
}
