using System.Text.RegularExpressions;

namespace HotelPOS.Api.Tests;

// Phase 9 § 10 static checks that run with every `dotnet test`, so no separate CI step can be forgotten.
public class StaticSecurityChecksTests
{
    // EF Core turns an interpolated string passed straight to these methods into SQL parameters; the *Raw variants
    // and any string built beforehand (concatenation, string.Format, a variable) would not be parameterised.
    private static readonly Regex RawSqlCall = new(@"\b(FromSqlRaw|ExecuteSqlRaw|ExecuteSqlRawAsync|SqlQueryRaw)\s*[<(]", RegexOptions.Compiled);

    private static readonly Regex SafeSqlCall = new(
        @"\b(SqlQuery<[^>]+>|FromSql|FromSqlInterpolated|ExecuteSql|ExecuteSqlAsync|ExecuteSqlInterpolated|ExecuteSqlInterpolatedAsync)\s*\(\s*(?<arg>\S{0,4})",
        RegexOptions.Compiled);

    [Fact]
    public void SourceCode_NeverBuildsSqlFromStrings()
    {
        var problems = new List<string>();
        foreach (var file in SourceFiles())
        {
            var text = File.ReadAllText(file);
            foreach (Match match in RawSqlCall.Matches(text))
            {
                problems.Add($"{Relative(file)}: {match.Groups[1].Value} is not parameterised");
            }

            foreach (Match match in SafeSqlCall.Matches(text))
            {
                if (!match.Groups["arg"].Value.StartsWith('$'))
                {
                    problems.Add($"{Relative(file)}: {match.Groups[1].Value} must take an interpolated string literal directly");
                }
            }
        }

        problems.Should().BeEmpty();
    }

    // Connection-string passwords belong in user-secrets, HOTELPOS_TEST_POSTGRES or the installer's protected config;
    // placeholders such as "Password=<password>" in documentation are fine.
    private static readonly Regex ConnectionStringPassword = new(@"\bPassword=(?!<)[^;""\s]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [Fact]
    public void Repository_ContainsNoConnectionStringPasswords()
    {
        var root = RepositoryRoot();
        var files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Where(f => !IsBuildOutput(f) && !Path.GetFileName(f).Equals("appsettings.Local.json", StringComparison.OrdinalIgnoreCase));

        var leaks = files
            .SelectMany(f => File.ReadLines(f).Select((line, index) => (File: f, Line: index + 1, Text: line)))
            .Where(l => ConnectionStringPassword.IsMatch(l.Text))
            .Select(l => $"{Relative(l.File)}:{l.Line}")
            .ToList();

        leaks.Should().BeEmpty("passwords must not be committed (see docs/dev-setup.md)");
    }

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                // Migrations run fixed DDL/DML written at design time, never request data.
                && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string Relative(string path) => Path.GetRelativePath(RepositoryRoot(), path);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "HotelPOS.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the tests run inside the repository");
        return directory!.FullName;
    }
}
