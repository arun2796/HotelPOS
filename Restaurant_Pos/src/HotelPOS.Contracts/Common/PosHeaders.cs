namespace HotelPOS.Contracts.Common;

/// <summary>Custom HTTP headers exchanged between clients and the API.</summary>
public static class PosHeaders
{
    public const string CorrelationId = "X-Correlation-Id";
    public const string DeviceId = "X-Device-Id";
    public const string MachineName = "X-Machine-Name";
    public const string IdempotencyKey = "Idempotency-Key";
}
