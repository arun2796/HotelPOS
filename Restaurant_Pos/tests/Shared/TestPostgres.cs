namespace HotelPOS.Testing;

/// <summary>
/// Connection strings for throw-away test databases on the developer's PostgreSQL server. Set
/// HOTELPOS_TEST_POSTGRES (a connection string without Database) to use another server or account.
/// Every database created here is named hotelpos_*, so test clean-up never touches other databases.
/// </summary>
internal static class TestPostgres
{
    public const string ServerVariable = "HOTELPOS_TEST_POSTGRES";

    private const string DefaultServer = "Host=localhost;Port=5432;Username=postgres;Password=arun27";

    public static string Server =>
        Environment.GetEnvironmentVariable(ServerVariable) is { Length: > 0 } configured ? configured : DefaultServer;

    /// <summary>A connection string for a new, uniquely named database (created by EF migrations).</summary>
    public static string NewDatabase(string purpose) =>
        $"{Server.TrimEnd(';')};Database=hotelpos_{purpose.ToLowerInvariant()}_{Guid.NewGuid():N}";
}
