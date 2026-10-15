using System.Windows;
using HotelPOS.Contracts.Enums;
using HotelPOS.Desktop.Services.Configuration;
using MaterialDesignThemes.Wpf;

namespace HotelPOS.Desktop.Services.Ui;


public sealed class ThemeService
{
    public const string Light = "Light";
    public const string Dark = "Dark";
    public const string Comfort = "Comfort";
    public const string Compact = "Compact";

    private readonly IClientSettingsService _settings;

    public ThemeService(IClientSettingsService settings)
    {
        _settings = settings;
    }

    public string Current { get; private set; } = Light;

    public string CurrentDensity { get; private set; } = Compact;

    public bool IsDark => Current == Dark;

    // Touch terminals (waiter tablets, kitchen screens) get Comfort unless the terminal chose otherwise.
    public static string ResolveDensity(ClientSettings settings)
    {
        if (string.Equals(settings.Density, Comfort, StringComparison.OrdinalIgnoreCase))
        {
            return Comfort;
        }

        if (string.Equals(settings.Density, Compact, StringComparison.OrdinalIgnoreCase))
        {
            return Compact;
        }

        return settings.DeviceType is DeviceType.Waiter or DeviceType.Kitchen ? Comfort : Compact;
    }

    public void ApplySaved()
    {
        var settings = _settings.Current;
        Apply(settings.Theme);
        ApplyDensity(ResolveDensity(settings));
    }

    public void Toggle()
    {
        Apply(IsDark ? Light : Dark);
        var settings = _settings.Current;
        settings.Theme = Current;
        _settings.Save(settings);
    }

    public void Apply(string? theme)
    {
        Current = string.Equals(theme, Dark, StringComparison.OrdinalIgnoreCase) ? Dark : Light;
        if (OwnApplication() is not { } app)
        {
            return;
        }

        Swap(app, "Themes/Tokens.", $"Themes/Tokens.{Current}.xaml");

        // MaterialDesign's own brushes (calendar, context menus) follow the same base theme.
        foreach (var palette in app.Resources.MergedDictionaries.OfType<CustomColorTheme>())
        {
            palette.BaseTheme = IsDark ? BaseTheme.Dark : BaseTheme.Light;
        }

        foreach (Window window in app.Windows)
        {
            WindowBackdrop.Apply(window, IsDark);
        }
    }

    public void ApplyDensity(string? density)
    {
        CurrentDensity = string.Equals(density, Comfort, StringComparison.OrdinalIgnoreCase) ? Comfort : Compact;
        if (OwnApplication() is { } app)
        {
            Swap(app, "Themes/Density.", $"Themes/Density.{CurrentDensity}.xaml");
        }
    }

    // Application resources belong to the UI thread. A call from any other thread (view-model tests running beside
    // the rendering test) only records the choice and leaves the resources alone.
    private static Application? OwnApplication() =>
        Application.Current is { } app && app.Dispatcher.CheckAccess() ? app : null;

    // Applies the title bar colour and Mica backdrop once the window has a handle.
    public void Attach(Window window)
    {
        if (!WindowBackdrop.Apply(window, IsDark))
        {
            window.SourceInitialized += (_, _) => WindowBackdrop.Apply(window, IsDark);
        }
    }

    private static void Swap(Application app, string marker, string path)
    {
        var dictionaries = app.Resources.MergedDictionaries;
        var replacement = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/HotelPOS.Desktop;component/{path}", UriKind.Absolute),
        };
        var existing = dictionaries.FirstOrDefault(d => d.Source?.OriginalString.Contains(marker, StringComparison.Ordinal) == true);
        if (existing is null)
        {
            dictionaries.Add(replacement);
        }
        else if (existing.Source!.OriginalString.EndsWith(path, StringComparison.Ordinal))
        {
            return;
        }
        else
        {
            dictionaries[dictionaries.IndexOf(existing)] = replacement;
        }
    }
}
