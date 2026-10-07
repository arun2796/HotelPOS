namespace HotelPOS.Testing;

internal static class TestPostgres
{
    public const string ServerVariable = "HOTELPOS_TEST_POSTGRES";

    private const string DefaultServer = "Host=localhost;Port=5432;Username=postgres;Password=arun27";

    public static string Server =>
        Environment.GetEnvironmentVariable(ServerVariable) is { Length: > 0 } configured ? configured : DefaultServer;

    public static string NewDatabase(string purpose) =>
        $"{Server.TrimEnd(';')};Database=hotelpos_{purpose.ToLowerInvariant()}_{Guid.NewGuid():N}";
}
