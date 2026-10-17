using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace HotelPOS.Client.Tests;

/// <summary>
/// Phase 21 guard rails: the web tokens (wwwroot/css/tokens.css) carry the same colours as the desktop theme
/// dictionaries, the two dark blocks are identical, and Blazor pages take colours from variables, not hex literals.
/// </summary>
public sealed class WebTokenContractTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    // web variable -> desktop key
    private static readonly (string Var, string Key)[] Mapped =
    {
        ("bg", "Brush.Background"), ("surface", "Brush.Surface"), ("surface-alt", "Brush.SurfaceAlt"),
        ("surface-subtle", "Brush.SurfaceSubtle"), ("layer-hover", "Brush.LayerHover"),
        ("border", "Brush.Border"), ("border-subtle", "Brush.BorderSubtle"), ("border-strong", "Brush.BorderStrong"),
        ("text", "Brush.TextPrimary"), ("text-2", "Brush.TextSecondary"), ("text-3", "Brush.TextTertiary"),
        ("input-bg", "Brush.InputBackground"), ("input-border", "Brush.InputBorder"), ("focus", "Brush.Focus"),
        ("primary", "Brush.Primary"), ("primary-hover", "Brush.PrimaryHover"), ("primary-soft", "Brush.PrimarySoft"),
        ("on-primary", "Brush.PrimaryText"),
        ("info", "Brush.Info"), ("info-soft", "Brush.InfoSoft"), ("info-text", "Brush.InfoText"),
        ("success", "Brush.Success"), ("success-soft", "Brush.SuccessSoft"), ("success-text", "Brush.SuccessText"),
        ("warning", "Brush.Warning"), ("warning-soft", "Brush.WarningSoft"), ("warning-text", "Brush.WarningText"),
        ("danger", "Brush.Danger"), ("danger-soft", "Brush.DangerSoft"), ("danger-text", "Brush.DangerText"),
        ("neutral", "Brush.Neutral"), ("neutral-soft", "Brush.NeutralSoft"), ("neutral-text", "Brush.NeutralText"),
        ("sidebar", "Brush.Sidebar"), ("sidebar-text", "Brush.SidebarText"), ("sidebar-text-active", "Brush.SidebarTextActive"),
        ("sidebar-text-muted", "Brush.SidebarTextMuted"), ("sidebar-hover", "Brush.SidebarHover"),
        ("sidebar-selected", "Brush.SidebarSelected"), ("sidebar-accent", "Brush.SidebarAccent"),
        ("sidebar-divider", "Brush.SidebarDivider"), ("sidebar-badge", "Brush.SidebarBadge"),
        ("st-available", "Brush.Status.Available"), ("st-occupied", "Brush.Status.Occupied"),
        ("st-ordering", "Brush.Status.Ordering"), ("st-preparing", "Brush.Status.Preparing"),
        ("st-ready", "Brush.Status.Ready"), ("st-billing", "Brush.Status.Billing"), ("st-out", "Brush.Status.OutOfService"),
    };

    [Fact]
    public void LightVariables_EqualTheDesktopLightKeys()
    {
        var web = Block(":root {");
        var desktop = Colours("Tokens.Light.xaml");

        foreach (var (name, key) in Mapped)
        {
            web.Should().ContainKey(name);
            web[name].Should().Be(desktop[key], $"--{name} must equal {key} (light)");
        }
    }

    [Fact]
    public void DarkVariables_EqualTheDesktopDarkKeys()
    {
        // The dark blocks only restate what changes; everything else is inherited from :root.
        var web = new Dictionary<string, string>(Block(":root {"));
        foreach (var pair in Block(":root[data-theme=\"dark\"] {"))
        {
            web[pair.Key] = pair.Value;
        }

        var desktop = Colours("Tokens.Dark.xaml");
        foreach (var (name, key) in Mapped)
        {
            web[name].Should().Be(desktop[key], $"--{name} must equal {key} (dark)");
        }
    }

    [Fact]
    public void DarkMediaBlock_EqualsTheDataThemeBlock()
    {
        var media = Block(":root:not([data-theme=\"light\"]) {");
        var attribute = Block(":root[data-theme=\"dark\"] {");

        media.Should().NotBeEmpty();
        media.Should().BeEquivalentTo(attribute, "system-dark and explicit-dark must look the same");
    }

    [Fact]
    public void Tokens_DeclareTheTypographyAndTouchScale()
    {
        var css = File.ReadAllText(TokensPath());
        var root = Block(":root {");

        root["touch"].Should().Be("48PX");
        root["font"].Should().StartWith("\"INTER\"", "values are upper-cased by the parser");
        foreach (var name in new[] { "fs-title", "fs-section", "fs-body", "fs-label", "fs-caption", "fs-amount", "fs-amount-lg", "fs-kpi" })
        {
            root.Should().ContainKey(name);
        }

        css.Should().Contain("font-display: swap");
        foreach (var weight in new[] { "Regular", "Medium", "SemiBold", "Bold" })
        {
            File.Exists(Path.Combine(Path.GetDirectoryName(TokensPath())!, "..", "fonts", $"Inter-{weight}.woff2")).Should().BeTrue();
        }
    }

    [Fact]
    public void RazorFiles_UseTokensNotHexLiterals()
    {
        var shared = Path.Combine(RepositoryRoot(), "src", "HotelPOS.Client.Shared");
        var literal = new Regex(@"#[0-9A-Fa-f]{3,8}\b", RegexOptions.Compiled);
        var sep = Path.DirectorySeparatorChar;

        var offenders = Directory.EnumerateFiles(shared, "*.razor", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}obj{sep}") && !f.Contains($"{sep}bin{sep}"))
            .Where(f => literal.IsMatch(File.ReadAllText(f)))
            .Select(f => Path.GetRelativePath(shared, f))
            .ToList();

        offenders.Should().BeEmpty("pages must use var(--token) so both themes stay consistent");
    }

    // The declarations (upper-cased) of the first block that starts with `selector`.
    private static Dictionary<string, string> Block(string selector)
    {
        var css = File.ReadAllText(TokensPath());
        var start = css.IndexOf(selector, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"tokens.css must contain {selector}");
        var end = css.IndexOf('}', start);
        var body = css.Substring(start + selector.Length, end - start - selector.Length);
        body = Regex.Replace(body, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

        return Regex.Matches(body, @"--([a-z0-9-]+):\s*([^;]+);")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.Trim().ToUpperInvariant());
    }

    private static Dictionary<string, string> Colours(string file) =>
        XDocument.Load(Path.Combine(RepositoryRoot(), "src", "HotelPOS.Desktop", "Themes", file)).Root!.Elements()
            .Where(e => e.Attribute(X + "Key") is not null && e.Attribute("Color") is not null)
            .ToDictionary(e => (string)e.Attribute(X + "Key")!, e => ((string)e.Attribute("Color")!).ToUpperInvariant(), StringComparer.Ordinal);

    private static string TokensPath() =>
        Path.Combine(RepositoryRoot(), "src", "HotelPOS.Client.Shared", "wwwroot", "css", "tokens.css");

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
