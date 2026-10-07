namespace HotelPOS.Application.Common;

/// <summary>JWT settings, bound from the "Jwt" configuration section.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "HotelPOS";
    public string Audience { get; set; } = "HotelPOS.Clients";

    /// <summary>HMAC-SHA256 signing key. At least 32 characters. Generated per installation.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 60;
    public int RefreshTokenHours { get; set; } = 12;
}

/// <summary>General application settings, bound from the "App" configuration section.</summary>
public sealed class AppOptions
{
    public const string SectionName = "App";

    /// <summary>Oldest desktop version allowed to connect (enforced from Phase 10).</summary>
    public string MinClientVersion { get; set; } = "0.1.0";

    /// <summary>Filled at startup from the API assembly version.</summary>
    public string ApiVersion { get; set; } = "0.0.0";
}
