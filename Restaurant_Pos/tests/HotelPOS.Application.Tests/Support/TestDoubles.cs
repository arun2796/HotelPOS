using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Contracts.Security;

namespace HotelPOS.Application.Tests.Support;

public sealed class TestClock : IClock
{
    public static readonly DateTime Start = new(2026, 10, 7, 6, 30, 0, DateTimeKind.Utc);

    public DateTime UtcNow { get; set; } = Start;

    public void Reset() => UtcNow = Start;

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}

public sealed class TestCurrentUser : ICurrentUser
{
    // Requests sent concurrently from one test can come from different terminals (each async flow sets its own).
    private static readonly AsyncLocal<Guid?> DeviceOfFlow = new();

    private Guid? _deviceId;

    public bool IsAuthenticated => UserId is not null;
    public int? UserId { get; set; }
    public string? UserName { get; set; }
    public IReadOnlyList<string> Roles { get; set; } = Array.Empty<string>();

    public Guid? DeviceId
    {
        get => DeviceOfFlow.Value ?? _deviceId;
        set => _deviceId = value;
    }

    public static void UseDeviceInThisFlow(Guid deviceId) => DeviceOfFlow.Value = deviceId;
    public string? DeviceName { get; set; }
    public string? MachineName { get; set; } = "TEST-PC";
    public string? IpAddress { get; set; } = "127.0.0.1";
    public string? CorrelationId { get; set; } = "test-correlation";

    // Defaults of the signed-in roles, as a fresh token would carry; tests may narrow it.
    public IReadOnlyList<string> Permissions { get; set; } = Array.Empty<string>();

    public bool IsInRole(string role) => Roles.Contains(role);

    public bool HasPermission(string permission) =>
        Roles.Contains(Contracts.Security.Roles.Admin) || Permissions.Contains(permission);

    public void SignIn(int userId, string userName, params string[] roles)
    {
        UserId = userId;
        UserName = userName;
        Roles = roles;
        Permissions = RolePermissionMap.For(roles);
    }

    public void Reset()
    {
        UserId = null;
        UserName = null;
        Roles = Array.Empty<string>();
        Permissions = Array.Empty<string>();
        DeviceId = null;
        DeviceName = null;
    }
}

public sealed class RecordingRealtimeNotifier : IRealtimeNotifier
{
    private readonly List<(string EventName, object Payload, RealtimeAudience Audience)> _events = new();

    public IReadOnlyList<(string EventName, object Payload, RealtimeAudience Audience)> Events
    {
        get
        {
            lock (_events)
            {
                return _events.ToList();
            }
        }
    }

    public Task PublishAsync(string eventName, object payload, RealtimeAudience audience, CancellationToken cancellationToken = default)
    {
        lock (_events)
        {
            _events.Add((eventName, payload, audience));
        }

        return Task.CompletedTask;
    }

    public void Reset()
    {
        lock (_events)
        {
            _events.Clear();
        }
    }
}

public sealed class RecordingConnectionTracker : IOnlineDeviceTracker
{
    public List<int> DisconnectedUsers { get; } = new();

    public IReadOnlySet<Guid> OnlineDeviceIds() => new HashSet<Guid>();

    public bool IsDeviceOnline(Guid deviceId) => false;

    public Task<int> DisconnectDeviceAsync(Guid deviceId, CancellationToken cancellationToken = default) => Task.FromResult(0);

    public Task<int> DisconnectUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        DisconnectedUsers.Add(userId);
        return Task.FromResult(1);
    }

    public void Reset() => DisconnectedUsers.Clear();
}
