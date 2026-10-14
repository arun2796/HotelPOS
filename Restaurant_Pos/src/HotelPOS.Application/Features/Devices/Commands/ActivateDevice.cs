using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Security;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HotelPOS.Application.Features.Devices.Commands;

public sealed record ActivateDeviceCommand(Guid Id) : ICommand, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.DevicesManage;
}

internal sealed class ActivateDeviceHandler : ICommandHandler<ActivateDeviceCommand>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly IAfterCommit _afterCommit;
    private readonly IDeviceAccessCache _deviceAccess;
    private readonly IOnlineDeviceTracker _onlineTracker;
    private readonly IRealtimeNotifier _realtime;
    private readonly IClock _clock;
    private readonly ILogger<ActivateDeviceHandler> _logger;

    public ActivateDeviceHandler(
        IAppDbContext db,
        IAuditTrail audit,
        IAfterCommit afterCommit,
        IDeviceAccessCache deviceAccess,
        IOnlineDeviceTracker onlineTracker,
        IRealtimeNotifier realtime,
        IClock clock,
        ILogger<ActivateDeviceHandler> logger)
    {
        _db = db;
        _audit = audit;
        _afterCommit = afterCommit;
        _deviceAccess = deviceAccess;
        _onlineTracker = onlineTracker;
        _realtime = realtime;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result> Handle(ActivateDeviceCommand command, CancellationToken cancellationToken)
    {
        var id = command.Id;
        var device = await _db.Devices.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (device is null)
        {
            return AppErrors.NotFound("Device", id);
        }

        var now = _clock.UtcNow;
        device.Activate();

        _audit.Add(AuditActions.DeviceActivated, nameof(Device), device.Id.ToString(),
            newValues: new { device.Name, ActivatedAt = now });

        await _db.SaveChangesAsync(cancellationToken);

        _afterCommit.Enqueue(async () =>
        {
            _deviceAccess.Remember(device.Id, deactivated: false);
            await _realtime.PublishAsync(
                HubEvents.DeviceStatusChanged,
                new DeviceStatusChangedEvent
                {
                    DeviceId = device.Id,
                    DeviceName = device.Name,
                    IsOnline = _onlineTracker.IsDeviceOnline(id),
                    IsActive = true,
                    TimestampUtc = now,
                },
                RealtimeAudience.ForRoles(Roles.Admin, Roles.Manager),
                cancellationToken);

            _logger.LogInformation("Device '{DeviceName}' ({DeviceId}) was activated", device.Name, id);
        });

        return Result.Success();
    }
}
