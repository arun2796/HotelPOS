using System.Collections.Concurrent;

namespace HotelPOS.Api.Hubs;

public sealed record HubConnectionInfo(
    string ConnectionId,
    int UserId,
    string UserName,
    IReadOnlyList<string> Roles,
    Guid? DeviceId,
    string? DeviceName,
    DateTime ConnectedAtUtc);

/// <summary>In-memory presence: which users and devices currently hold a hub connection.</summary>
public sealed class ConnectionTracker
{
    private readonly ConcurrentDictionary<string, HubConnectionInfo> _connections = new();

    public int Count => _connections.Count;

    public void Add(HubConnectionInfo info) => _connections[info.ConnectionId] = info;

    public HubConnectionInfo? Remove(string connectionId) =>
        _connections.TryRemove(connectionId, out var info) ? info : null;

    public IReadOnlyCollection<HubConnectionInfo> Snapshot() => _connections.Values.ToList();

    public IReadOnlySet<Guid> OnlineDeviceIds() =>
        _connections.Values.Where(c => c.DeviceId is not null).Select(c => c.DeviceId!.Value).ToHashSet();

    public bool IsDeviceOnline(Guid deviceId) =>
        _connections.Values.Any(c => c.DeviceId == deviceId);
}
