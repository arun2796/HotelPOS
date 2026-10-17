using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using HotelPOS.Contracts.Presentation;
using HotelPOS.Desktop.Controls;
using HotelPOS.Desktop.Converters;

namespace HotelPOS.Desktop.Tests;

/// <summary>Phase 21 0.5: the logic behind the desktop primitives (the templates themselves are rendered by ScreenRenderingTests).</summary>
public class PrimitiveTests
{
    [Theory]
    [InlineData(0, 20, 186, "1–20 of 186")]
    [InlineData(1, 20, 186, "21–40 of 186")]
    [InlineData(9, 20, 186, "181–186 of 186")]
    [InlineData(0, 20, 0, "0 of 0")]
    public void Pager_DescribesTheVisibleRange(int index, int size, int total, string expected)
    {
        Pager.Describe(index, size, total).Should().Be(expected);
    }

    [Theory]
    [InlineData(12.5, "▲ 12.5%")]
    [InlineData(-3.14, "▼ 3.1%")]
    [InlineData(0.04, "0%")]
    [InlineData(100, "▲ 100%")]
    public void KpiTile_ChangeAlwaysCarriesAnArrowOrZero(double delta, string expected)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            KpiTile.DescribeDelta(delta).Should().Be(expected);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("Masala dosa", "MD")]
    [InlineData("Tea", "T")]
    [InlineData("  ", "?")]
    [InlineData("1/2 Biryani", "1B")]
    public void OrderLine_FallsBackToInitials(string name, string expected)
    {
        OrderLine.InitialsOf(name).Should().Be(expected);
    }

    [Theory]
    [InlineData(StatusTone.Success, ToneBrushKind.Text, "Brush.SuccessText")]
    [InlineData(StatusTone.Warning, ToneBrushKind.Soft, "Brush.WarningSoft")]
    [InlineData(StatusTone.Danger, ToneBrushKind.Solid, "Brush.Danger")]
    [InlineData(StatusTone.Neutral, ToneBrushKind.Soft, "Brush.NeutralSoft")]
    [InlineData(StatusTone.Info, ToneBrushKind.Text, "Brush.InfoText")]
    public void ToneBrushes_MapToTheTokenKeys(StatusTone tone, ToneBrushKind kind, string key)
    {
        ToneBrushes.Key(tone, kind).Should().Be(key);
    }

    [Fact]
    public void ToneBrushes_SolidUsesWhiteTextAndSoftUsesToneText()
    {
        ToneBrushes.For(StatusTone.Success, StatusFill.Solid).Should().Be(("Brush.Success", null));
        ToneBrushes.For(StatusTone.Warning, StatusFill.Soft).Should().Be(("Brush.WarningSoft", "Brush.WarningText"));
    }

    [Fact]
    public void EveryIconTheStatusMapEmits_HasADesktopGeometry()
    {
        var root = DesktopRoot();
        var icons = XDocument.Load(Path.Combine(root, "Resources", "Icons.xaml")).Descendants()
            .Select(e => (string?)e.Attribute("{http://schemas.microsoft.com/winfx/2006/xaml}Key"))
            .Where(k => k is not null && k.StartsWith("Icon.", StringComparison.Ordinal))
            .Select(k => k![5..])
            .ToHashSet();

        var source = File.ReadAllText(Path.Combine(root, "..", "HotelPOS.Contracts", "Presentation", "StatusPresentation.cs"));
        var emitted = Regex.Matches(source, @"StatusTone\.\w+, ""([a-z-]+)""").Select(m => m.Groups[1].Value).Distinct().ToList();
        var required = emitted.Concat(new[]
        {
            "home", "table", "receipt", "plus", "more", "bell", "search", "clock", "flame", "check", "x", "chevron-left", "chevron-right",
            "user", "settings", "signal", "wifi-off", "star", "edit", "trash", "menu", "sun", "moon", "logout",
        });

        emitted.Should().NotBeEmpty();
        required.Where(name => !icons.Contains(name)).Should().BeEmpty("every status icon name needs a geometry in Resources/Icons.xaml");
    }

    private static string DesktopRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HotelPOS.sln")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("HotelPOS.sln"), "src", "HotelPOS.Desktop");
    }
}
