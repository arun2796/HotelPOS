namespace HotelPOS.Application.Common;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "HotelPOS";
    public string Audience { get; set; } = "HotelPOS.Clients";

    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 60;
    public int RefreshTokenHours { get; set; } = 12;
}

public sealed class AppOptions
{
    public const string SectionName = "App";

    public string MinClientVersion { get; set; } = "0.1.0";

    public string ApiVersion { get; set; } = "0.0.0";
}
