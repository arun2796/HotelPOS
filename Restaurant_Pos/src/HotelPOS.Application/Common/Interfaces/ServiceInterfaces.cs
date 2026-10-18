using HotelPOS.Domain.Identity;

namespace HotelPOS.Application.Common.Interfaces;

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

    string GenerateRefreshToken();

    string HashRefreshToken(string refreshToken);
}

public sealed record AuditActor(int? UserId, string? UserName);

public interface IAuditService
{
    void Record(
        string action,
        string entityType,
        string? entityId,
        object? oldValues = null,
        object? newValues = null,
        AuditActor? actor = null);
}

public interface IOnlineDeviceTracker
{
    IReadOnlySet<Guid> OnlineDeviceIds();
    bool IsDeviceOnline(Guid deviceId);

    // Closes every open real-time connection of the device; returns how many were closed.
    Task<int> DisconnectDeviceAsync(Guid deviceId, CancellationToken cancellationToken = default);

    // Closes every open real-time connection of the user (deactivated account); returns how many were closed.
    Task<int> DisconnectUserAsync(int userId, CancellationToken cancellationToken = default);
}

// Phase 22 P4: one live real-time (hub) connection, as the presence snapshot reports it.
public sealed record PresenceEntry(
    int UserId,
    string UserName,
    IReadOnlyList<string> Roles,
    Guid? DeviceId,
    string? DeviceName,
    DateTime ConnectedSinceUtc);

// Snapshot of who is connected right now. In-memory on the API process: empty after a restart, so it is "live", not attendance.
public interface IPresenceTracker
{
    IReadOnlyList<PresenceEntry> Snapshot();
}
