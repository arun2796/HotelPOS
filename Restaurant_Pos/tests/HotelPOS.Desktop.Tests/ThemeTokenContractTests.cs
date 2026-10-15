using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace HotelPOS.Desktop.Tests;

/// <summary>
/// Phase 17 guard rails for the design system: both theme dictionaries define exactly the same keys (a screen that
/// looks right in light mode cannot silently lose a brush in dark mode), and screens take their colours from tokens
/// rather than hard-coded hex values.
/// </summary>
public sealed class ThemeTokenContractTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    // Files allowed to carry literal colours, with the reason.
    private static readonly string[] HardCodedColourAllowList =
    {
        // The invoice preview is a sheet of white paper with dark ink in both themes, like the printed invoice.
        "Modules/Billing/BillDetailView.xaml",
    };

    [Fact]
    public void LightAndDarkTokens_DefineTheSameKeys()
    {
        var light = Keys("Tokens.Light.xaml");
        var dark = Keys("Tokens.Dark.xaml");

        dark.Except(light).Should().BeEmpty("every dark token must exist in the light theme");
        light.Except(dark).Should().BeEmpty("every light token must exist in the dark theme");
        light.Should().Contain(new[] { "Brush.Primary", "Brush.Sidebar", "Brush.Status.Ready", "Shadow.Popup" });
    }

    [Fact]
    public void TableAndTicketStatusColours_AreTheSameInBothThemes()
    {
        var light = Colours("Tokens.Light.xaml");
        var dark = Colours("Tokens.Dark.xaml");

        foreach (var key in light.Keys.Where(k => k.StartsWith("Brush.Status.", StringComparison.Ordinal)))
        {
            dark[key].Should().Be(light[key], $"{key} must mean the same thing on every terminal");
        }
    }

    [Fact]
    public void Screens_DoNotHardCodeColours()
    {
        var desktop = Path.Combine(RepositoryRoot(), "src", "HotelPOS.Desktop");
        var literal = new Regex("\"#[0-9A-Fa-f]{6,8}\"", RegexOptions.Compiled);

        var offenders = new[] { "Modules", "Shell" }
            .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(desktop, folder), "*.xaml", SearchOption.AllDirectories))
            .Select(file => new { Relative = Path.GetRelativePath(desktop, file).Replace('\\', '/'), Text = File.ReadAllText(file) })
            .Where(f => !HardCodedColourAllowList.Contains(f.Relative))
            .Where(f => literal.IsMatch(f.Text))
            .Select(f => f.Relative)
            .ToList();

        offenders.Should().BeEmpty("screens must use theme tokens (Brush.*) so both themes stay consistent");
    }

    private static HashSet<string> Keys(string file) =>
        XDocument.Load(ThemePath(file)).Root!.Elements()
            .Select(e => (string?)e.Attribute(X + "Key"))
            .Where(k => k is not null)
            .ToHashSet(StringComparer.Ordinal)!;

    private static Dictionary<string, string> Colours(string file) =>
        XDocument.Load(ThemePath(file)).Root!.Elements()
            .Where(e => e.Attribute(X + "Key") is not null && e.Attribute("Color") is not null)
            .ToDictionary(e => (string)e.Attribute(X + "Key")!, e => ((string)e.Attribute("Color")!).ToUpperInvariant(), StringComparer.Ordinal);

    private static string ThemePath(string file) =>
        Path.Combine(RepositoryRoot(), "src", "HotelPOS.Desktop", "Themes", file);

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
