using HotelPOS.Domain.Identity;

namespace HotelPOS.Application.Common.Interfaces;

/// <summary>Source of the current time. Never use DateTime.Now in Domain/Application code.</summary>
public interface IClock
{
    DateTime UtcNow { get; }
}

public enum PasswordCheck
{
    Failed,
    Success,
    SuccessRehashNeeded,
}

public interface IPasswordHasher
{
    string Hash(string password);

    PasswordCheck Verify(string passwordHash, string providedPassword);
}

public sealed record IssuedAccessToken(string Token, DateTime ExpiresAtUtc);

public interface ITokenService
{
    IssuedAccessToken CreateAccessToken(
        User user,
        IReadOnlyList<string> roles,
        IReadOnlyList<string> permissions,
        Guid? deviceId,
        string? deviceName);

    /// <summary>Creates a new random refresh token (returned to the client once, never stored in clear).</summary>
    string GenerateRefreshToken();

    string HashRefreshToken(string refreshToken);
}

/// <summary>Who performed an audited action when it is not the authenticated caller (e.g. login).</summary>
public sealed record AuditActor(int? UserId, string? UserName);

public interface IAuditService
{
    /// <summary>
    /// Adds an audit entry to the current unit of work. It is saved together with the business change
    /// by the caller's SaveChangesAsync, so both succeed or fail together.
    /// </summary>
    void Record(
        string action,
        string entityType,
        string? entityId,
        object? oldValues = null,
        object? newValues = null,
        AuditActor? actor = null);
}
