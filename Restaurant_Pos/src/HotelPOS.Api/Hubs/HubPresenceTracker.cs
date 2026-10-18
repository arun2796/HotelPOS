using HotelPOS.Application.Common.Interfaces;

namespace HotelPOS.Api.Hubs;

// Adapter: exposes the hub's ConnectionTracker snapshot to the Application layer (Phase 22 P4).
public sealed class HubPresenceTracker : IPresenceTracker
{
    private readonly ConnectionTracker _tracker;

    public HubPresenceTracker(ConnectionTracker tracker)
    {
        _tracker = tracker;
    }

    public IReadOnlyList<PresenceEntry> Snapshot() =>
        _tracker.Snapshot()
            .Select(c => new PresenceEntry(c.UserId, c.UserName, c.Roles, c.DeviceId, c.DeviceName, c.ConnectedAtUtc))
            .ToList();
}
