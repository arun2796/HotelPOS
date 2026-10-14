using HotelPOS.Contracts.Enums;

namespace HotelPOS.Contracts.Auth;

public sealed record LoginRequest
{
    public string Username { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;

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

public sealed record ApprovalTokenRequest
{
    public string ApproverUsername { get; init; } = string.Empty;
    public string ApproverPassword { get; init; } = string.Empty;
    public string ActionType { get; init; } = string.Empty;
}

public sealed record ApprovalTokenResponse
{
    public string Token { get; init; } = string.Empty;
    public DateTime ExpiresAtUtc { get; init; }
    public int ApproverId { get; init; }
    public string ApproverUsername { get; init; } = string.Empty;
    public string ApproverDisplayName { get; init; } = string.Empty;
    public string ActionType { get; init; } = string.Empty;
}

public static class PasswordPolicy
{
    public const int MinLength = 8;
    public const int MaxLength = 100;

    public static bool MeetsComplexity(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinLength)
        {
            return false;
        }

        var hasLetter = password.Any(char.IsLetter);
        var hasDigit = password.Any(char.IsDigit);
        return hasLetter && hasDigit;
    }
}
