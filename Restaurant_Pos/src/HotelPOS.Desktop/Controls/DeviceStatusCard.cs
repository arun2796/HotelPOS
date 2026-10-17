using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HotelPOS.Contracts.Presentation;
using HotelPOS.Desktop.Converters;

namespace HotelPOS.Desktop.Controls;

// Sidebar footer card: the connection as a status badge, this terminal's name and the app version. In a collapsed sidebar
// (IsCompact) it shrinks to one tone-coloured icon. Colours follow the live theme (resource references).
public sealed class DeviceStatusCard : Control
{
    public static readonly DependencyProperty DisplayProperty =
        DependencyProperty.Register(nameof(Display), typeof(StatusDisplay), typeof(DeviceStatusCard),
            new PropertyMetadata(default(StatusDisplay), (d, _) => ((DeviceStatusCard)d).Refresh()));

    public static readonly DependencyProperty TerminalNameProperty =
        DependencyProperty.Register(nameof(TerminalName), typeof(string), typeof(DeviceStatusCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty VersionProperty =
        DependencyProperty.Register(nameof(Version), typeof(string), typeof(DeviceStatusCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IsCompactProperty =
        DependencyProperty.Register(nameof(IsCompact), typeof(bool), typeof(DeviceStatusCard), new PropertyMetadata(false));

    public static readonly DependencyProperty ToneBrushProperty =
        DependencyProperty.Register(nameof(ToneBrush), typeof(Brush), typeof(DeviceStatusCard), new PropertyMetadata(null));

    static DeviceStatusCard()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(DeviceStatusCard), new FrameworkPropertyMetadata(typeof(DeviceStatusCard)));
        FocusableProperty.OverrideMetadata(typeof(DeviceStatusCard), new FrameworkPropertyMetadata(false));
    }

    public StatusDisplay Display
    {
        get => (StatusDisplay)GetValue(DisplayProperty);
        set => SetValue(DisplayProperty, value);
    }

    public string TerminalName
    {
        get => (string)GetValue(TerminalNameProperty);
        set => SetValue(TerminalNameProperty, value);
    }

    public string Version
    {
        get => (string)GetValue(VersionProperty);
        set => SetValue(VersionProperty, value);
    }

    public bool IsCompact
    {
        get => (bool)GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    /// <summary>The tone's solid colour, for the compact icon.</summary>
    public Brush? ToneBrush
    {
        get => (Brush?)GetValue(ToneBrushProperty);
        set => SetValue(ToneBrushProperty, value);
    }

    private void Refresh() => SetResourceReference(ToneBrushProperty, ToneBrushes.Key(Display.Tone, ToneBrushKind.Solid));
}
