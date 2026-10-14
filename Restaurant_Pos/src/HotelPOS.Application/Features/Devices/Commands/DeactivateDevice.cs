using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Security;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HotelPOS.Application.Features.Devices.Commands;

// Locks a terminal out: its sessions are revoked, its open hub connections closed after a notice.
public sealed record DeactivateDeviceCommand(Guid Id) : ICommand, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.DevicesManage;
}

internal sealed class DeactivateDeviceHandler : ICommandHandler<DeactivateDeviceCommand>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly IAfterCommit _afterCommit;
    private readonly IDeviceAccessCache _deviceAccess;
    private readonly IOnlineDeviceTracker _onlineTracker;
    private readonly IRealtimeNotifier _realtime;
    private readonly IClock _clock;
    private readonly ILogger<DeactivateDeviceHandler> _logger;

    public DeactivateDeviceHandler(
        IAppDbContext db,
        IAuditTrail audit,
        IAfterCommit afterCommit,
        IDeviceAccessCache deviceAccess,
        IOnlineDeviceTracker onlineTracker,
        IRealtimeNotifier realtime,
        IClock clock,
        ILogger<DeactivateDeviceHandler> logger)
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

    public async Task<Result> Handle(DeactivateDeviceCommand command, CancellationToken cancellationToken)
    {
        var id = command.Id;
        var device = await _db.Devices.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (device is null)
        {
            return AppErrors.NotFound("Device", id);
        }

        var now = _clock.UtcNow;
        device.Deactivate(now);

        var tokens = await _db.RefreshTokens
            .Where(t => t.DeviceId == id && t.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var t in tokens)
        {
            t.Revoke(now, "Device deactivated");
        }

        _audit.Add(AuditActions.DeviceDeactivated, nameof(Device), device.Id.ToString(),
            newValues: new { device.Name, DeactivatedAt = now, RevokedSessions = tokens.Count });

        await _db.SaveChangesAsync(cancellationToken);

        _afterCommit.Enqueue(async () =>
        {
            // Set only after the commit: a request between a cache drop and the commit would re-cache "active" for good.
            _deviceAccess.Remember(device.Id, deactivated: true);

            // The notice goes out before the connection is closed, so the terminal can tell the user why.
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
        });

        return Result.Success();
    }
}
