namespace HotelPOS.Application.Common;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "HotelPOS";
    public string Audience { get; set; } = "HotelPOS.Clients";

    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 60;
    public int RefreshTokenHours { get; set; } = 12;

    public const int MinimumSigningKeyLength = 32;

    /// <summary>
    /// The value <c>appsettings.Production.template.json</c> ships; the installer replaces it with random bytes. A
    /// hand-copied template keeps it, and it is long enough to pass the length rule, so it is refused explicitly.
    /// </summary>
    public const string TemplatePlaceholderPrefix = "<";

    /// <summary>The key committed in <c>appsettings.Development.json</c>; public, so never valid outside Development.</summary>
    public const string DevelopmentKeyMarker = "DEVELOPMENT-ONLY";

    /// <summary>
    /// Why the signing key is unusable, or <c>null</c> when it is acceptable. Placeholders and the committed
    /// development key are rejected whatever their length; the development key is tolerated only when
    /// <paramref name="isDevelopment"/> is true.
    /// </summary>
    public static string? SigningKeyProblem(string? signingKey, bool isDevelopment)
    {
        if (string.IsNullOrWhiteSpace(signingKey) || signingKey.Length < MinimumSigningKeyLength)
        {
            return $"Jwt:SigningKey must be configured and at least {MinimumSigningKeyLength} characters long.";
        }

        if (signingKey.TrimStart().StartsWith(TemplatePlaceholderPrefix, StringComparison.Ordinal))
        {
            return "Jwt:SigningKey still holds the template placeholder. Run scripts\\configure-server.ps1 (or the installer) to generate a key.";
        }

        if (!isDevelopment && signingKey.Contains(DevelopmentKeyMarker, StringComparison.OrdinalIgnoreCase))
        {
            return "Jwt:SigningKey is the committed development key, which is public. Generate a key for this server.";
        }

        return null;
    }
}

public sealed class AppOptions
{
    public const string SectionName = "App";

    public string MinClientVersion { get; set; } = "0.1.0";

    public string ApiVersion { get; set; } = "0.0.0";
}
