using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HotelPOS.Contracts.Presentation;
using HotelPOS.Desktop.Converters;

namespace HotelPOS.Desktop.Controls;

// One figure on a dashboard or report: a coloured icon square (the tone), the label, the value at the KPI size with tabular
// figures, an optional change versus the previous period (▲/▼ + %, in a tone, never colour alone), a caption and an optional
// sparkline. It is a button so a tile can open the screen behind the figure; without a Command it is plain.
// Title/Value/Detail/Glyph are the original properties; Icon (a Controls/Icon kind) wins over Glyph when both are set.
public sealed class KpiTile : Button
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(KpiTile), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(string), typeof(KpiTile), new PropertyMetadata(string.Empty));

    /// <summary>The caption under the value.</summary>
    public static readonly DependencyProperty DetailProperty =
        DependencyProperty.Register(nameof(Detail), typeof(string), typeof(KpiTile), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(KpiTile), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(string), typeof(KpiTile), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ToneProperty =
        DependencyProperty.Register(nameof(Tone), typeof(StatusTone), typeof(KpiTile),
            new PropertyMetadata(StatusTone.Success, (d, _) => ((KpiTile)d).Refresh()));

    /// <summary>Change versus the previous period, in percent (12.5 = up 12.5 %); null hides the line.</summary>
    public static readonly DependencyProperty DeltaProperty =
        DependencyProperty.Register(nameof(Delta), typeof(double?), typeof(KpiTile),
            new PropertyMetadata(null, (d, _) => ((KpiTile)d).Refresh()));

    /// <summary>Set when a rise is bad news (cancellations, voids): the arrow keeps its direction, the tone flips.</summary>
    public static readonly DependencyProperty LowerIsBetterProperty =
        DependencyProperty.Register(nameof(LowerIsBetter), typeof(bool), typeof(KpiTile),
            new PropertyMetadata(false, (d, _) => ((KpiTile)d).Refresh()));

    /// <summary>Series for the sparkline (oldest first); fewer than two points hides it.</summary>
    public static readonly DependencyProperty SparkProperty =
        DependencyProperty.Register(nameof(Spark), typeof(IReadOnlyList<double>), typeof(KpiTile),
            new PropertyMetadata(null, (d, _) => ((KpiTile)d).Refresh()));

    private static readonly DependencyPropertyKey DeltaTextPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(DeltaText), typeof(string), typeof(KpiTile), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DeltaTextProperty = DeltaTextPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey SparkGeometryPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(SparkGeometry), typeof(Geometry), typeof(KpiTile), new PropertyMetadata(null));

    public static readonly DependencyProperty SparkGeometryProperty = SparkGeometryPropertyKey.DependencyProperty;

    public static readonly DependencyProperty IconBackgroundProperty =
        DependencyProperty.Register(nameof(IconBackground), typeof(Brush), typeof(KpiTile), new PropertyMetadata(null));

    public static readonly DependencyProperty IconForegroundProperty =
        DependencyProperty.Register(nameof(IconForeground), typeof(Brush), typeof(KpiTile), new PropertyMetadata(null));

    public static readonly DependencyProperty DeltaForegroundProperty =
        DependencyProperty.Register(nameof(DeltaForeground), typeof(Brush), typeof(KpiTile), new PropertyMetadata(null));

    static KpiTile()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(KpiTile), new FrameworkPropertyMetadata(typeof(KpiTile)));
    }

    public KpiTile()
    {
        Refresh();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string Detail
    {
        get => (string)GetValue(DetailProperty);
        set => SetValue(DetailProperty, value);
    }

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public string Icon
    {
        get => (string)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public StatusTone Tone
    {
        get => (StatusTone)GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }

    public double? Delta
    {
        get => (double?)GetValue(DeltaProperty);
        set => SetValue(DeltaProperty, value);
    }

    public bool LowerIsBetter
    {
        get => (bool)GetValue(LowerIsBetterProperty);
        set => SetValue(LowerIsBetterProperty, value);
    }

    public IReadOnlyList<double>? Spark
    {
        get => (IReadOnlyList<double>?)GetValue(SparkProperty);
        set => SetValue(SparkProperty, value);
    }

    /// <summary>"▲ 12.5%" / "▼ 3.1%" / "0%"; empty without a Delta.</summary>
    public string DeltaText => (string)GetValue(DeltaTextProperty);

    public Geometry? SparkGeometry => (Geometry?)GetValue(SparkGeometryProperty);

    public Brush? IconBackground
    {
        get => (Brush?)GetValue(IconBackgroundProperty);
        set => SetValue(IconBackgroundProperty, value);
    }

    public Brush? IconForeground
    {
        get => (Brush?)GetValue(IconForegroundProperty);
        set => SetValue(IconForegroundProperty, value);
    }

    public Brush? DeltaForeground
    {
        get => (Brush?)GetValue(DeltaForegroundProperty);
        set => SetValue(DeltaForegroundProperty, value);
    }

    /// <summary>The words for a change: an arrow, the size, a % sign. Zero (to one decimal) has no arrow.</summary>
    public static string DescribeDelta(double delta)
    {
        var rounded = Math.Round(Math.Abs(delta), 1);
        if (rounded == 0)
        {
            return "0%";
        }

        var arrow = delta > 0 ? "▲" : "▼";
        return string.Create(CultureInfo.CurrentCulture, $"{arrow} {rounded:0.#}%");
    }

    private void Refresh()
    {
        SetResourceReference(IconBackgroundProperty, ToneBrushes.Key(Tone, ToneBrushKind.Soft));
        SetResourceReference(IconForegroundProperty, ToneBrushes.Key(Tone, ToneBrushKind.Text));

        if (Delta is { } delta)
        {
            SetValue(DeltaTextPropertyKey, DescribeDelta(delta));
            var tone = Math.Round(delta, 1) == 0
                ? StatusTone.Neutral
                : (delta > 0) ^ LowerIsBetter ? StatusTone.Success : StatusTone.Danger;
            SetResourceReference(DeltaForegroundProperty, ToneBrushes.Key(tone, ToneBrushKind.Text));
        }
        else
        {
            SetValue(DeltaTextPropertyKey, string.Empty);
        }

        SetValue(SparkGeometryPropertyKey, Spark is { Count: >= 2 } series ? ChartGeometry.Sparkline(series) : null);
    }
}

// A row of KPI tiles in equal columns.
public sealed class KpiStrip : ItemsControl
{
    public static readonly DependencyProperty ColumnsProperty =
        DependencyProperty.Register(nameof(Columns), typeof(int), typeof(KpiStrip), new PropertyMetadata(3));

    public int Columns
    {
        get => (int)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }
}
