using HotelPOS.Application.Common.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace HotelPOS.Api.Hubs;

public sealed class SignalRRealtimeNotifier : IRealtimeNotifier
{
    private readonly IHubContext<RestaurantHub> _hub;
    private readonly ILogger<SignalRRealtimeNotifier> _logger;

    public SignalRRealtimeNotifier(IHubContext<RestaurantHub> hub, ILogger<SignalRRealtimeNotifier> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async Task PublishAsync(string eventName, object payload, RealtimeAudience audience, CancellationToken cancellationToken = default)
    {
        try
        {
            if (audience.Everyone)
            {
                await _hub.Clients.All.SendAsync(eventName, payload, cancellationToken);
                return;
            }

            var groups = audience.Roles.Select(HubGroups.Role)
                .Concat(audience.UserIds.Select(HubGroups.User))
                .Concat(audience.StationIds.Select(HubGroups.Station))
                .Concat(audience.DeviceIds.Select(HubGroups.Device))
                .Distinct()
                .ToList();

            if (groups.Count > 0)
            {
                await _hub.Clients.Groups(groups).SendAsync(eventName, payload, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            // The database is the source of truth; clients resync on reconnect, so a lost event is recoverable.
            _logger.LogWarning(ex, "Failed to publish real-time event {EventName}", eventName);
        }
    }
}
