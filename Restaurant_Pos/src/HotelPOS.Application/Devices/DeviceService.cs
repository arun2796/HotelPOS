using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Devices;
using HotelPOS.Contracts.Enums;
using HotelPOS.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Devices;

public interface IDeviceService
{
    Task<Result<DeviceDto>> RegisterAsync(RegisterDeviceRequest request, CancellationToken cancellationToken = default);

    Task<Device> UpsertAsync(
        string name,
        DeviceType type,
        string? machineName,
        string? appVersion,
        int? stationId,
        CancellationToken cancellationToken = default);

    Task TouchAsync(Guid deviceId, CancellationToken cancellationToken = default);
}

public sealed class DeviceService : IDeviceService
{
    private readonly IAppDbContext _db;
    private readonly IAuditService _audit;
    private readonly IClock _clock;

    public DeviceService(IAppDbContext db, IAuditService audit, IClock clock)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<DeviceDto>> RegisterAsync(RegisterDeviceRequest request, CancellationToken cancellationToken = default)
    {
        if (request.StationId is { } stationId
            && !await _db.PreparationStations.AnyAsync(s => s.Id == stationId && s.IsActive, cancellationToken))
        {
            return AppErrors.Validation("stationId", $"Preparation station {stationId} does not exist or is inactive.");
        }

        var device = await UpsertAsync(request.Name, request.Type, request.MachineName, request.AppVersion, request.StationId, cancellationToken);
        if (!device.IsActive)
        {
            return AppErrors.DeviceDisabled(device.Name);
        }

        device.Touch(_clock.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(device);
    }

    public async Task<Device> UpsertAsync(
        string name,
        DeviceType type,
        string? machineName,
        string? appVersion,
        int? stationId,
        CancellationToken cancellationToken = default)
    {
        var normalized = Device.Normalize(name);
        var device = await _db.Devices.FirstOrDefaultAsync(d => d.Name == normalized, cancellationToken);
        if (device is null)
        {
            device = new Device(Guid.NewGuid(), normalized, type, _clock.UtcNow);
            _db.Devices.Add(device);
            _audit.Record(AuditActions.DeviceRegistered, nameof(Device), device.Id.ToString(),
                newValues: new { device.Name, Type = type.ToString(), MachineName = machineName });
        }

        device.UpdateRegistration(type, machineName, appVersion, stationId ?? device.StationId);
        return device;
    }

    public async Task TouchAsync(Guid deviceId, CancellationToken cancellationToken = default)
    {
        var device = await _db.Devices.FirstOrDefaultAsync(d => d.Id == deviceId, cancellationToken);
        if (device is null)
        {
            return;
        }

        device.Touch(_clock.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
    }

    internal static DeviceDto ToDto(Device device, bool isOnline = false) => new()
    {
        Id = device.Id,
        Name = device.Name,
        Type = device.Type,
        MachineName = device.MachineName,
        AppVersion = device.AppVersion,
        StationId = device.StationId,
        IsActive = device.IsActive,
        IsOnline = isOnline,
        LastSeenAtUtc = device.LastSeenAt,
        RegisteredAtUtc = device.RegisteredAt,
    };
}
