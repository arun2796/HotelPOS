using HotelPOS.Contracts.Common;

namespace HotelPOS.Contracts.Admin;

public sealed record AuditLogDto
{
    public long Id { get; init; }
    public int? UserId { get; init; }
    public string? UserName { get; init; }
    public Guid? DeviceId { get; init; }
    public string? DeviceName { get; init; }
    public string? MachineName { get; init; }
    public string? IpAddress { get; init; }
    public string Action { get; init; } = string.Empty;
    public string EntityType { get; init; } = string.Empty;
    public string? EntityId { get; init; }
    public string? OldValues { get; init; }
    public string? NewValues { get; init; }
    public string? CorrelationId { get; init; }
    public DateTime TimestampUtc { get; init; }
}

public sealed record AuditLogQuery : PagedQuery
{
    public int? UserId { get; init; }
    public string? UserName { get; init; }
    public Guid? DeviceId { get; init; }
    public string? Action { get; init; }
    public string? EntityType { get; init; }
    public string? EntityId { get; init; }
    public string? Search { get; init; }
    public DateTime? From { get; init; }
    public DateTime? To { get; init; }
}

