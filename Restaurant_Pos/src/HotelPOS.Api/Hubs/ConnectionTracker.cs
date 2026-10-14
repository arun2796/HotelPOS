using System.Collections.Concurrent;
using HotelPOS.Application.Common.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace HotelPOS.Api.Hubs;

public sealed record HubConnectionInfo(
    string ConnectionId,
    int UserId,
    string UserName,
    IReadOnlyList<string> Roles,
    Guid? DeviceId,
    string? DeviceName,
    DateTime ConnectedAtUtc);

public sealed class ConnectionTracker : IOnlineDeviceTracker
{
    // Long enough for a ServerNotice written just before the abort to reach the client on a LAN.
    private static readonly TimeSpan NoticeGrace = TimeSpan.FromMilliseconds(500);

    private readonly ConcurrentDictionary<string, (HubConnectionInfo Info, HubCallerContext Context)> _connections = new();

    public int Count => _connections.Count;

    public void Add(HubConnectionInfo info, HubCallerContext context) => _connections[info.ConnectionId] = (info, context);

    public HubConnectionInfo? Remove(string connectionId) =>
        _connections.TryRemove(connectionId, out var entry) ? entry.Info : null;

    public IReadOnlyCollection<HubConnectionInfo> Snapshot() => _connections.Values.Select(e => e.Info).ToList();

    public IReadOnlySet<Guid> OnlineDeviceIds() =>
        _connections.Values.Where(e => e.Info.DeviceId is not null).Select(e => e.Info.DeviceId!.Value).ToHashSet();

    public bool IsDeviceOnline(Guid deviceId) =>
        _connections.Values.Any(e => e.Info.DeviceId == deviceId);

    public Task<int> DisconnectDeviceAsync(Guid deviceId, CancellationToken cancellationToken = default) =>
        AbortAsync(e => e.DeviceId == deviceId, cancellationToken);

    public Task<int> DisconnectUserAsync(int userId, CancellationToken cancellationToken = default) =>
        AbortAsync(e => e.UserId == userId, cancellationToken);

    private async Task<int> AbortAsync(Func<HubConnectionInfo, bool> match, CancellationToken cancellationToken)
    {
        var targets = _connections.Values.Where(e => match(e.Info)).Select(e => e.Context).ToList();
        if (targets.Count == 0)
        {
            return 0;
        }

        await Task.Delay(NoticeGrace, cancellationToken);
        foreach (var context in targets)
        {
            context.Abort();
        }

        return targets.Count;
    }
}
