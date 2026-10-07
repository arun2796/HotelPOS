using HotelPOS.Application.Common.Interfaces;

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
    public bool IsAuthenticated => UserId is not null;
    public int? UserId { get; set; }
    public string? UserName { get; set; }
    public IReadOnlyList<string> Roles { get; set; } = Array.Empty<string>();
    public Guid? DeviceId { get; set; }
    public string? DeviceName { get; set; }
    public string? MachineName { get; set; } = "TEST-PC";
    public string? IpAddress { get; set; } = "127.0.0.1";
    public string? CorrelationId { get; set; } = "test-correlation";

    public bool IsInRole(string role) => Roles.Contains(role);

    public void SignIn(int userId, string userName, params string[] roles)
    {
        UserId = userId;
        UserName = userName;
        Roles = roles;
    }

    public void Reset()
    {
        UserId = null;
        UserName = null;
        Roles = Array.Empty<string>();
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
