using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Security;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Devices;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HotelPOS.Application.Devices;

public sealed class DeviceAdminService : IDeviceAdminService
{
    private readonly IDeviceAccessCache _deviceAccess;
    private readonly IAppDbContext _db;
    private readonly IOnlineDeviceTracker _onlineTracker;
    private readonly IRealtimeNotifier _realtime;
    private readonly IAuditService _audit;
    private readonly IClock _clock;
    private readonly ILogger<DeviceAdminService> _logger;

    public DeviceAdminService(
        IDeviceAccessCache deviceAccess,
        IAppDbContext db,
        IOnlineDeviceTracker onlineTracker,
        IRealtimeNotifier realtime,
        IAuditService audit,
        IClock clock,
        ILogger<DeviceAdminService> logger)
    {
        _deviceAccess = deviceAccess;
        _db = db;
        _onlineTracker = onlineTracker;
        _realtime = realtime;
        _audit = audit;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<DeviceDto>>> GetAllDevicesAsync(CancellationToken cancellationToken = default)
    {
        var devices = await _db.Devices.AsNoTracking()
            .OrderBy(d => d.Name)
            .ToListAsync(cancellationToken);

        var onlineIds = _onlineTracker.OnlineDeviceIds();

        var dtos = devices.Select(d => ToDto(d, onlineIds.Contains(d.Id))).ToList();
        return dtos;
    }

    public async Task<Result<DeviceDto>> GetDeviceByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var device = await _db.Devices.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (device is null)
        {
            return AppErrors.NotFound("Device", id);
        }

        var isOnline = _onlineTracker.IsDeviceOnline(id);
        return ToDto(device, isOnline);
    }

    public async Task<Result> UpdateDeviceAsync(Guid id, UpdateDeviceRequest request, CancellationToken cancellationToken = default)
    {
        var device = await _db.Devices.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (device is null)
        {
            return AppErrors.NotFound("Device", id);
        }

        var normalized = Device.Normalize(request.Name);
        if (await _db.Devices.AnyAsync(d => d.Name == normalized && d.Id != id, cancellationToken))
        {
            return AppErrors.Duplicate($"Another device is already named '{request.Name.Trim()}'.");
        }

        var oldName = device.Name;
        var oldStation = device.StationId;

        device.Rename(request.Name);
        device.AssignStation(request.StationId);

        _audit.Record(AuditActions.DeviceUpdated, nameof(Device), device.Id.ToString(),
            oldValues: new { Name = oldName, StationId = oldStation },
            newValues: new { Name = device.Name, StationId = device.StationId });

        await _db.SaveChangesAsync(cancellationToken);

        var isOnline = _onlineTracker.IsDeviceOnline(id);
        await _realtime.PublishAsync(
            HubEvents.DeviceStatusChanged,
            new DeviceStatusChangedEvent
            {
                DeviceId = device.Id,
                DeviceName = device.Name,
                IsOnline = isOnline,
                IsActive = device.IsActive,
                TimestampUtc = _clock.UtcNow,
            },
            RealtimeAudience.ForRoles(Roles.Admin, Roles.Manager),
            cancellationToken);

        return Result.Success();
    }

    public async Task<Result> DeactivateDeviceAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var device = await _db.Devices.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (device is null)
        {
            return AppErrors.NotFound("Device", id);
        }

        var now = _clock.UtcNow;
        device.Deactivate(now);

        // Revoke active sessions for this device
        var tokens = await _db.RefreshTokens
            .Where(t => t.DeviceId == id && t.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var t in tokens)
        {
            t.Revoke(now, "Device deactivated");
        }

        _audit.Record(AuditActions.DeviceDeactivated, nameof(Device), device.Id.ToString(),
            newValues: new { device.Name, DeactivatedAt = now, RevokedSessions = tokens.Count });

        await _db.SaveChangesAsync(cancellationToken);

        // Set only after the commit: a request between a cache drop and the commit would re-cache "active" for good.
        _deviceAccess.Remember(device.Id, deactivated: true);

        // Send ServerNotice to the device before connection is closed
        await _realtime.PublishAsync(
            HubEvents.ServerNotice,
            new ServerNoticeEvent
            {
                Message = "This terminal has been disabled by an administrator.",
                Code = ErrorCodes.DeviceDisabled,
            },
            new RealtimeAudience { DeviceIds = new[] { id } },
            cancellationToken);

        // Refusing new connections is not enough: an open connection would keep receiving every broadcast.
        var dropped = await _onlineTracker.DisconnectDeviceAsync(id, cancellationToken);

        // Broadcast status change to admins and managers
        await _realtime.PublishAsync(
            HubEvents.DeviceStatusChanged,
            new DeviceStatusChangedEvent
            {
                DeviceId = device.Id,
                DeviceName = device.Name,
                IsOnline = false,
                IsActive = false,
                TimestampUtc = now,
            },
            RealtimeAudience.ForRoles(Roles.Admin, Roles.Manager),
            cancellationToken);

        _logger.LogInformation("Device '{DeviceName}' ({DeviceId}) was deactivated; {Dropped} connection(s) closed", device.Name, id, dropped);
        return Result.Success();
    }

    public async Task<Result> ActivateDeviceAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var device = await _db.Devices.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (device is null)
        {
            return AppErrors.NotFound("Device", id);
        }

        var now = _clock.UtcNow;
        device.Activate();

        _audit.Record(AuditActions.DeviceActivated, nameof(Device), device.Id.ToString(),
            newValues: new { device.Name, ActivatedAt = now });

        await _db.SaveChangesAsync(cancellationToken);
        _deviceAccess.Remember(device.Id, deactivated: false);

        var isOnline = _onlineTracker.IsDeviceOnline(id);
        await _realtime.PublishAsync(
            HubEvents.DeviceStatusChanged,
            new DeviceStatusChangedEvent
            {
                DeviceId = device.Id,
                DeviceName = device.Name,
                IsOnline = isOnline,
                IsActive = true,
                TimestampUtc = now,
            },
            RealtimeAudience.ForRoles(Roles.Admin, Roles.Manager),
            cancellationToken);

        _logger.LogInformation("Device '{DeviceName}' ({DeviceId}) was activated", device.Name, id);
        return Result.Success();
    }

    private static DeviceDto ToDto(Device device, bool isOnline) => new()
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
        DeactivatedAtUtc = device.DeactivatedAt,
    };
}

