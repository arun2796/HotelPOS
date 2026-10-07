namespace HotelPOS.Application.Common.Interfaces;

/// <summary>Who should receive a real-time event. Combine several targets; duplicates are fine.</summary>
public sealed record RealtimeAudience
{
    public bool Everyone { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
    public IReadOnlyList<int> UserIds { get; init; } = Array.Empty<int>();
    public IReadOnlyList<int> StationIds { get; init; } = Array.Empty<int>();
    public IReadOnlyList<Guid> DeviceIds { get; init; } = Array.Empty<Guid>();

    public static RealtimeAudience All { get; } = new() { Everyone = true };

    public static RealtimeAudience ForRoles(params string[] roles) => new() { Roles = roles };
}

/// <summary>
/// Publishes notifications to connected clients. Call it only after the database transaction has
/// committed; failures are logged and never fail the business operation.
/// </summary>
public interface IRealtimeNotifier
{
    Task PublishAsync(string eventName, object payload, RealtimeAudience audience, CancellationToken cancellationToken = default);
}
