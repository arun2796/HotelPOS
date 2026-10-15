using System.Globalization;
using HotelPOS.Contracts.Admin;
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
    // The shortest minimum length an admin may configure, and the longest password accepted at all.
    public const int MinLength = 8;
    public const int MaxLength = 100;
}

// The password rule an admin configures in Settings (Password* keys); the default is the original
// "8 characters, letters and digits". Shared so the server and the desktop describe the same rule.
public sealed record PasswordRules
{
    public int MinLength { get; init; } = PasswordPolicy.MinLength;
    public bool RequireLetter { get; init; } = true;
    public bool RequireDigit { get; init; } = true;
    public bool RequireUpperAndLower { get; init; }
    public bool RequireSymbol { get; init; }

    public static PasswordRules Default { get; } = new();

    // Reads the rule from a settings lookup; missing or unreadable values keep the default, the length is kept in range.
    public static PasswordRules FromSettings(Func<string, string?> setting)
    {
        bool Flag(string key, bool fallback) => bool.TryParse(setting(key), out var v) ? v : fallback;

        var min = int.TryParse(setting(SettingKeys.PasswordMinLength), NumberStyles.Integer, CultureInfo.InvariantCulture, out var length)
            ? length
            : PasswordPolicy.MinLength;
        return new PasswordRules
        {
            MinLength = Math.Clamp(min, PasswordPolicy.MinLength, PasswordPolicy.MaxLength),
            RequireLetter = Flag(SettingKeys.PasswordRequireLetter, true),
            RequireDigit = Flag(SettingKeys.PasswordRequireDigit, true),
            RequireUpperAndLower = Flag(SettingKeys.PasswordRequireUpperAndLower, false),
            RequireSymbol = Flag(SettingKeys.PasswordRequireSymbol, false),
        };
    }

    // "Password must be at least 8 characters and contain letters and digits."
    public string Describe()
    {
        var kinds = new List<string>();
        if (RequireUpperAndLower)
        {
            kinds.Add("both upper and lower case letters");
        }
        else if (RequireLetter)
        {
            kinds.Add("letters");
        }

        if (RequireDigit)
        {
            kinds.Add("digits");
        }

        if (RequireSymbol)
        {
            kinds.Add("a symbol");
        }

        var text = $"Password must be at least {MinLength} characters";
        return kinds.Count == 0
            ? text + "."
            : $"{text} and contain {(kinds.Count == 1 ? kinds[0] : string.Join(", ", kinds.Take(kinds.Count - 1)) + " and " + kinds[^1])}.";
    }

    public bool IsMet(string? password) =>
        !string.IsNullOrEmpty(password)
        && password.Length >= MinLength
        && (!(RequireLetter || RequireUpperAndLower) || password.Any(char.IsLetter))
        && (!RequireDigit || password.Any(char.IsDigit))
        && (!RequireUpperAndLower || (password.Any(char.IsUpper) && password.Any(char.IsLower)))
        && (!RequireSymbol || password.Any(c => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c)));

    // Null when the password meets the rule, otherwise the message to show.
    public string? Check(string? password) => IsMet(password) ? null : Describe();
}
