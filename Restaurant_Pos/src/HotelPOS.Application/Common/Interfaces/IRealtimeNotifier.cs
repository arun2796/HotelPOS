namespace HotelPOS.Application.Common.Interfaces;

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

public interface IRealtimeNotifier
{
    Task PublishAsync(string eventName, object payload, RealtimeAudience audience, CancellationToken cancellationToken = default);
}
