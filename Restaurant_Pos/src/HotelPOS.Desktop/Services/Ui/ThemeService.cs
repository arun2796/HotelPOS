using System.Windows;
using HotelPOS.Desktop.Services.Configuration;

namespace HotelPOS.Desktop.Services.Ui;

/// <summary>Switches between the light and dark colour dictionaries at runtime.</summary>
public sealed class ThemeService
{
    public const string Light = "Light";
    public const string Dark = "Dark";

    private readonly IClientSettingsService _settings;

    public ThemeService(IClientSettingsService settings)
    {
        _settings = settings;
    }

    public string Current { get; private set; } = Light;

    public void ApplySaved() => Apply(_settings.Current.Theme);

    public void Toggle()
    {
        Apply(Current == Light ? Dark : Light);
        var settings = _settings.Current;
        settings.Theme = Current;
        _settings.Save(settings);
    }

    public void Apply(string? theme)
    {
        Current = string.Equals(theme, Dark, StringComparison.OrdinalIgnoreCase) ? Dark : Light;
        var dictionaries = Application.Current?.Resources.MergedDictionaries;
        if (dictionaries is null)
        {
            return;
        }

        var source = new Uri($"pack://application:,,,/HotelPOS.Desktop;component/Themes/Colors.{Current}.xaml", UriKind.Absolute);
        var existing = dictionaries.FirstOrDefault(d => d.Source?.OriginalString.Contains("Colors.", StringComparison.Ordinal) == true);
        var replacement = new ResourceDictionary { Source = source };
        if (existing is null)
        {
            dictionaries.Insert(0, replacement);
        }
        else
        {
            dictionaries[dictionaries.IndexOf(existing)] = replacement;
        }
    }
}
