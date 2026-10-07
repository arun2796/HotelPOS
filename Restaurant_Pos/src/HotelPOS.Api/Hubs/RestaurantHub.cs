using System.Globalization;
using System.Security.Claims;
using HotelPOS.Application.Common.Security;
using HotelPOS.Application.Devices;
using HotelPOS.Contracts.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace HotelPOS.Api.Hubs;

[Authorize]
public sealed class RestaurantHub : Hub
{
    private readonly ConnectionTracker _tracker;
    private readonly IDeviceService _devices;
    private readonly ILogger<RestaurantHub> _logger;

    public RestaurantHub(ConnectionTracker tracker, IDeviceService devices, ILogger<RestaurantHub> logger)
    {
        _tracker = tracker;
        _devices = devices;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var user = Context.User!;
        var userId = int.Parse(user.FindFirstValue(PosClaimTypes.Subject)!, CultureInfo.InvariantCulture);
        var roles = user.FindAll(PosClaimTypes.Role).Select(c => c.Value).ToList();
        var deviceId = Guid.TryParse(user.FindFirstValue(PosClaimTypes.DeviceId), out var id) ? id : (Guid?)null;
        var deviceName = user.FindFirstValue(PosClaimTypes.DeviceName);

        foreach (var role in roles)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.Role(role));
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.User(userId));
        if (deviceId is { } device)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.Device(device));
            await TouchDeviceAsync(device);
        }

        _tracker.Add(new HubConnectionInfo(
            Context.ConnectionId,
            userId,
            user.FindFirstValue(PosClaimTypes.Name) ?? string.Empty,
            roles,
            deviceId,
            deviceName,
            DateTime.UtcNow));

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
            await TouchDeviceAsync(deviceId);
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

    private async Task TouchDeviceAsync(Guid deviceId)
    {
        try
        {
            await _devices.TouchAsync(deviceId);
        }
        catch (Exception ex)
        {
            // Presence bookkeeping must never break the connection.
            _logger.LogWarning(ex, "Could not update last-seen time of device {DeviceId}", deviceId);
        }
    }
}
