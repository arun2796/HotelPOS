using HotelPOS.Contracts.Enums;

namespace HotelPOS.Contracts.Auth;

public sealed record LoginRequest
{
    public string Username { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;

    /// <summary>Device identity from the client configuration, e.g. WAITER-01. Optional.</summary>
    public string? DeviceName { get; init; }
    public DeviceType? DeviceType { get; init; }
    public string? MachineName { get; init; }
    public string? AppVersion { get; init; }
}

public sealed record LoginResponse
{
    public string AccessToken { get; init; } = string.Empty;
    public DateTime AccessTokenExpiresAtUtc { get; init; }
    public string RefreshToken { get; init; } = string.Empty;
    public DateTime RefreshTokenExpiresAtUtc { get; init; }
    public CurrentUserDto User { get; init; } = new();

    /// <summary>Server-side id of the device this session is bound to, when a device name was sent.</summary>
    public Guid? DeviceId { get; init; }
}

public sealed record CurrentUserDto
{
    public int Id { get; init; }
    public string Username { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Permissions { get; init; } = Array.Empty<string>();
    public bool MustChangePassword { get; init; }
}

public sealed record RefreshTokenRequest
{
    public string RefreshToken { get; init; } = string.Empty;
}

public sealed record LogoutRequest
{
    public string RefreshToken { get; init; } = string.Empty;
}

public sealed record ChangePasswordRequest
{
    public string CurrentPassword { get; init; } = string.Empty;
    public string NewPassword { get; init; } = string.Empty;
}

public static class PasswordPolicy
{
    /// <summary>Minimum password length. Phase 9 raises this and adds complexity rules.</summary>
    public const int MinLength = 6;
    public const int MaxLength = 100;
}
