using HotelPOS.Domain.Common;

namespace HotelPOS.Domain.Identity;

/// <summary>
/// Long-lived token used to obtain new access tokens. Only a hash is stored. Tokens are rotated on
/// every refresh; presenting an already-rotated token revokes the whole chain (theft detection).
/// </summary>
public sealed class RefreshToken : BaseEntity
{
    private RefreshToken()
    {
    }

    public RefreshToken(int userId, string tokenHash, Guid? deviceId, DateTime expiresAtUtc, string? createdByIp)
    {
        UserId = userId;
        TokenHash = tokenHash;
        DeviceId = deviceId;
        ExpiresAt = expiresAtUtc;
        CreatedByIp = createdByIp;
    }

    public int UserId { get; private set; }
    public User? User { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public Guid? DeviceId { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public string? RevokedReason { get; private set; }
    public string? ReplacedByTokenHash { get; private set; }
    public string? CreatedByIp { get; private set; }

    public bool IsRevoked => RevokedAt is not null;

    public bool IsActive(DateTime nowUtc) => !IsRevoked && ExpiresAt > nowUtc;

    public void Revoke(DateTime nowUtc, string reason, string? replacedByTokenHash = null)
    {
        if (IsRevoked)
        {
            return;
        }

        RevokedAt = nowUtc;
        RevokedReason = reason;
        ReplacedByTokenHash = replacedByTokenHash;
    }
}
