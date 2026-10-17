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


    // Phase 21: the owner's palette (light theme), pinned so a drive-by edit cannot drift it.
    [Theory]
    [InlineData("Brush.Primary", "#078747")]
    [InlineData("Brush.PrimaryHover", "#066B39")]
    [InlineData("Brush.Focus", "#2563EB")]
    [InlineData("Brush.Sidebar", "#142235")]
    [InlineData("Brush.SidebarSelected", "#078747")]
    [InlineData("Brush.Background", "#F4F7FB")]
    [InlineData("Brush.Surface", "#FFFFFF")]
    [InlineData("Brush.TextPrimary", "#172B4D")]
    [InlineData("Brush.TextSecondary", "#64748B")]
    [InlineData("Brush.Border", "#E2E8F0")]
    [InlineData("Brush.Success", "#16A34A")]
    [InlineData("Brush.SuccessText", "#15803D")]
    [InlineData("Brush.Warning", "#F59E0B")]
    [InlineData("Brush.WarningText", "#B45309")]
    [InlineData("Brush.Danger", "#DC2626")]
    [InlineData("Brush.Info", "#2563EB")]
    [InlineData("Brush.Neutral", "#94A3B8")]
    [InlineData("Brush.NeutralSoft", "#F1F5F9")]
    [InlineData("Brush.NeutralText", "#5B6B80")]
    public void LightPalette_MatchesTheOwnersMockups(string key, string expected) =>
        Colours("Tokens.Light.xaml")[key].Should().Be(expected);

    [Fact]
    public void StatusColours_FollowTheTones()
    {
        var light = Colours("Tokens.Light.xaml");

        light["Brush.Status.Ready"].Should().Be(light["Brush.Success"], "Ready is the solid success tone, no longer red (D8)");
        light["Brush.Status.Available"].Should().Be(light["Brush.Success"]);
        light["Brush.Status.Occupied"].Should().Be(light["Brush.Info"]);
        light["Brush.Status.Ordering"].Should().Be(light["Brush.Info"]);
        light["Brush.Status.Billing"].Should().Be(light["Brush.Warning"]);
        light["Brush.Status.Preparing"].Should().Be("#B45309", "Preparing keeps amber 700 so it stays apart from Billing");
        light["Brush.Status.OutOfService"].Should().Be(light["Brush.Neutral"]);
    }

    public static IEnumerable<object[]> ContrastPairs() =>
        new[]
        {
            // text on the surfaces it sits on
            ("Brush.TextPrimary", "Brush.Surface"), ("Brush.TextPrimary", "Brush.Background"),
            ("Brush.TextSecondary", "Brush.Surface"),
            ("Brush.Link", "Brush.Surface"),
            // tone text on its soft fill
            ("Brush.SuccessText", "Brush.SuccessSoft"), ("Brush.WarningText", "Brush.WarningSoft"),
            ("Brush.DangerText", "Brush.DangerSoft"), ("Brush.InfoText", "Brush.InfoSoft"),
            ("Brush.NeutralText", "Brush.NeutralSoft"),
            // sidebar
            ("Brush.SidebarText", "Brush.Sidebar"), ("Brush.SidebarTextActive", "Brush.Sidebar"),
            ("Brush.SidebarTextMuted", "Brush.Sidebar"), ("Brush.SidebarTextActive", "Brush.SidebarSelected"),
            ("Brush.SidebarText", "Brush.SidebarHover"),
            // text on the solid fills
            ("Brush.PrimaryText", "Brush.Primary"),
        }.SelectMany(p => new[] { new object[] { "Tokens.Light.xaml", p.Item1, p.Item2 }, new object[] { "Tokens.Dark.xaml", p.Item1, p.Item2 } });

    [Theory]
    [MemberData(nameof(ContrastPairs))]
    public void TextColours_MeetWcagAaOnTheirBackground(string theme, string foreground, string background)
    {
        var colours = Colours(theme);
        var ratio = Contrast(colours[foreground], colours[background]);

        ratio.Should().BeGreaterThanOrEqualTo(4.5, $"{foreground} on {background} in {theme} must be readable (WCAG AA)");
    }

    // The owner's #64748B on the #F4F7FB page measures 4.42:1 (4.76 on white cards): kept as specified, floor pinned at 4.4 so it cannot get worse.
    [Fact]
    public void SecondaryTextOnPageBackground_StaysAtTheOwnersContrast() =>
        Contrast(Colours("Tokens.Light.xaml")["Brush.TextSecondary"], Colours("Tokens.Light.xaml")["Brush.Background"]).Should().BeGreaterThanOrEqualTo(4.4);

    [Fact]
    public void FontSizes_ExistInBothDensities()
    {
        foreach (var density in new[] { "Density.Comfort.xaml", "Density.Compact.xaml" })
        {
            Keys(density).Should().Contain(new[]
            {
                "Font.Size.PageTitle", "Font.Size.Section", "Font.Size.Amount", "Font.Size.AmountLarge", "Font.Size.Kpi", "Font.Size.GridHeader",
            });
        }
    }

    // WCAG 2.x relative luminance contrast ratio of two #RRGGBB colours.
    internal static double Contrast(string foreground, string background)
    {
        var a = Luminance(foreground);
        var b = Luminance(background);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static double Luminance(string hex)
    {
        double Channel(int offset)
        {
            var c = Convert.ToInt32(hex.Substring(offset, 2), 16) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        hex = hex.TrimStart('#');
        hex = hex.Length == 8 ? hex[2..] : hex;
        return (0.2126 * Channel(0)) + (0.7152 * Channel(2)) + (0.0722 * Channel(4));
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
