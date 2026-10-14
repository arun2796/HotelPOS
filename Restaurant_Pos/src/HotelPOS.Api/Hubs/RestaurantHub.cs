using System.Globalization;
using System.Security.Claims;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Security;
using HotelPOS.Application.Devices;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace HotelPOS.Api.Hubs;

[Authorize]
public sealed class RestaurantHub : Hub
{
    private readonly ConnectionTracker _tracker;
    private readonly IDeviceService _devices;
    private readonly IRealtimeNotifier _realtime;
    private readonly ILogger<RestaurantHub> _logger;

    public RestaurantHub(ConnectionTracker tracker, IDeviceService devices, IRealtimeNotifier realtime, ILogger<RestaurantHub> logger)
    {
        _tracker = tracker;
        _devices = devices;
        _realtime = realtime;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var user = Context.User!;
        var userId = int.Parse(user.FindFirstValue(PosClaimTypes.Subject)!, CultureInfo.InvariantCulture);
        var roles = user.FindAll(PosClaimTypes.Role).Select(c => c.Value).ToList();
        var deviceId = Guid.TryParse(user.FindFirstValue(PosClaimTypes.DeviceId), out var id) ? id : (Guid?)null;
        var deviceName = user.FindFirstValue(PosClaimTypes.DeviceName);
        var deviceWasOnline = deviceId is { } known && _tracker.IsDeviceOnline(known);

        // Tracked before the device check: a deactivation that commits after the check below still finds this
        // connection in the tracker and closes it, so no connection slips through between the two.
        _tracker.Add(
            new HubConnectionInfo(
                Context.ConnectionId,
                userId,
                user.FindFirstValue(PosClaimTypes.Name) ?? string.Empty,
                roles,
                deviceId,
                deviceName,
                DateTime.UtcNow),
            Context);

        if (deviceId is { } device && await _devices.GetByIdAsync(device) is { IsActive: false })
        {
            _tracker.Remove(Context.ConnectionId);
            _logger.LogWarning("Deactivated device {DeviceId} attempted to connect to the hub; connection refused", device);
            Context.Abort();
            return;
        }

        foreach (var role in roles)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.Role(role));
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.User(userId));
        if (deviceId is { } online)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.Device(online));
            await DevicePresenceChangedAsync(online, deviceName, isOnline: true, announce: !deviceWasOnline);
        }

        _logger.LogInformation(
            "Hub connected: user {UserId} device {DeviceName} ({ConnectionId}); {Count} connection(s) online",
            userId, deviceName ?? "(none)", Context.ConnectionId, _tracker.Count);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var info = _tracker.Remove(Context.ConnectionId);
        if (info?.DeviceId is { } deviceId)
        {
            await DevicePresenceChangedAsync(deviceId, info.DeviceName, isOnline: false, announce: !_tracker.IsDeviceOnline(deviceId));
        }

        if (exception is null)
        {
            _logger.LogInformation("Hub disconnected: {ConnectionId}", Context.ConnectionId);
        }
        else
        {
            _logger.LogWarning(exception, "Hub connection lost: {ConnectionId}", Context.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    public DateTime Ping() => DateTime.UtcNow;

    // A kitchen screen bound to a station leaves the all-stations kitchen group, so it receives only its own tickets.
    public async Task JoinStation(int stationId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.Station(stationId));
        if (Context.User!.IsInRole(Roles.Kitchen))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, HubGroups.Role(Roles.Kitchen));
        }
    }

    public async Task LeaveStation(int stationId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, HubGroups.Station(stationId));
        if (Context.User!.IsInRole(Roles.Kitchen))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.Role(Roles.Kitchen));
        }
    }

    // Updates last-seen and, when the device's first connection opens or its last one closes, tells the admin screens.
    // A device can hold several connections (reconnect overlap), so only those two edges are announced.
    private async Task DevicePresenceChangedAsync(Guid deviceId, string? deviceName, bool isOnline, bool announce)
    {
        try
        {
            await _devices.TouchAsync(deviceId);
            if (!announce)
            {
                return;
            }

            // Read back so a disconnect caused by deactivation does not announce the device as active again.
            var device = await _devices.GetByIdAsync(deviceId);
            await _realtime.PublishAsync(
                HubEvents.DeviceStatusChanged,
                new DeviceStatusChangedEvent
                {
                    DeviceId = deviceId,
                    DeviceName = device?.Name ?? deviceName ?? string.Empty,
                    IsOnline = isOnline,
                    IsActive = device?.IsActive ?? false,
                    TimestampUtc = DateTime.UtcNow,
                },
                RealtimeAudience.ForRoles(Roles.Admin, Roles.Manager));
        }
        catch (Exception ex)
        {
            // Presence bookkeeping must never break the connection.
            _logger.LogWarning(ex, "Could not record presence of device {DeviceId}", deviceId);
        }
    }
}
