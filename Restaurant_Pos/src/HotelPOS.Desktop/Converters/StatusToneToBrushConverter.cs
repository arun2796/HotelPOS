using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using HotelPOS.Contracts.Presentation;
using HotelPOS.Desktop.Modules.Common;

namespace HotelPOS.Desktop.Converters;

/// <summary>Which of a tone's three brushes a view wants (Tokens.*.xaml: Brush.&lt;Tone&gt;, &lt;Tone&gt;Soft, &lt;Tone&gt;Text).</summary>
public enum ToneBrushKind
{
    Text,
    Soft,
    Solid,
}

/// <summary>Resource keys for a tone, shared by the converter and by controls that bind with SetResourceReference.</summary>
public static class ToneBrushes
{
    public static string Key(StatusTone tone, ToneBrushKind kind) => kind switch
    {
        ToneBrushKind.Soft => StatusLabels.SoftBrushKey(tone),
        ToneBrushKind.Solid => StatusLabels.SolidBrushKey(tone),
        _ => StatusLabels.TextBrushKey(tone),
    };

    /// <summary>Background and text keys of a display: soft = tinted fill with tone text; solid = tone fill with white text.</summary>
    public static (string Background, string? Foreground) For(StatusTone tone, StatusFill fill) =>
        fill == StatusFill.Solid
            ? (StatusLabels.SolidBrushKey(tone), null)
            : (StatusLabels.SoftBrushKey(tone), StatusLabels.TextBrushKey(tone));
}

/// <summary>
/// Tone to brush for data templates: value is a <see cref="StatusTone"/> (or a <see cref="StatusDisplay"/>), parameter is
/// Soft, Solid or Text (default). The brush is looked up when the binding evaluates, so it does not follow a live theme
/// switch; <c>StatusBadge</c> does, because it uses resource references.
/// </summary>
public sealed class StatusToneToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var tone = value switch
        {
            StatusTone t => t,
            StatusDisplay d => d.Tone,
            _ => StatusTone.Neutral,
        };
        var kind = parameter is string text && Enum.TryParse<ToneBrushKind>(text, true, out var parsed) ? parsed : ToneBrushKind.Text;
        return Application.Current?.TryFindResource(ToneBrushes.Key(tone, kind)) as Brush ?? Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
