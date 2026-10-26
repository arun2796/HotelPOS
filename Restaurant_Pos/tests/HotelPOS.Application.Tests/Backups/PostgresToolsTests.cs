using HotelPOS.Infrastructure.Services;

namespace HotelPOS.Application.Tests.Backups;

// Where pg_dump / pg_restore are looked for: a configured folder wins, the bundled pgsql\bin (Phase 26) is probed, and
// a missing tool is reported as null rather than an exception.
public class PostgresToolsTests : IDisposable
{
    private static readonly string Exe = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "hotelpos-pgtools-" + Guid.NewGuid().ToString("N"));

    public PostgresToolsTests() => Directory.CreateDirectory(_folder);

    [Fact]
    public void Find_uses_the_configured_folder_when_the_tool_is_there()
    {
        var expected = Path.Combine(_folder, "pg_dump" + Exe);
        File.WriteAllText(expected, "stub");

        Assert.Equal(expected, PostgresTools.Find("pg_dump", _folder));
    }

    [Fact]
    public void Find_returns_null_when_the_configured_folder_lacks_the_tool()
    {
        Assert.Null(PostgresTools.Find("pg_restore", _folder));
    }

    [Fact]
    public void Find_without_configuration_probes_without_throwing()
    {
        // PATH, the bundled pgsql\bin and Program Files are probed; any of them may or may not exist on the test machine.
        var found = PostgresTools.Find("pg_dump", null);
        Assert.True(found is null || File.Exists(found));
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { }
    }
}
