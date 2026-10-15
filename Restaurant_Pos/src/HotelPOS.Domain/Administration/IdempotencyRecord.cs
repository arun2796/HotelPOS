namespace HotelPOS.Domain.Administration;

public sealed class IdempotencyRecord
{
    public Guid Key { get; set; }
    public int? UserId { get; set; }
    public string Route { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public int? StatusCode { get; set; }
    public string? ResponseBody { get; set; }
    public bool InProgress { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
