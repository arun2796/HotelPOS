namespace HotelPOS.Domain.Administration;

public sealed class AuditLog
{
    public long Id { get; set; }
    public int? UserId { get; set; }
    public string? UserName { get; set; }
    public Guid? DeviceId { get; set; }
    public string? DeviceName { get; set; }
    public string? MachineName { get; set; }
    public string? IpAddress { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public string? CorrelationId { get; set; }
    public DateTime Timestamp { get; set; }
}
