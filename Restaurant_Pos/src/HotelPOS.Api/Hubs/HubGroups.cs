using System.Globalization;

namespace HotelPOS.Api.Hubs;

/// <summary>SignalR group names. See docs/04-realtime-events.md § 2.</summary>
public static class HubGroups
{
    public static string Role(string role) => "role:" + role.ToLowerInvariant();

    public static string User(int userId) => "user:" + userId.ToString(CultureInfo.InvariantCulture);

    public static string Device(Guid deviceId) => "device:" + deviceId.ToString("N");

    public static string Station(int stationId) => "station:" + stationId.ToString(CultureInfo.InvariantCulture);
}
