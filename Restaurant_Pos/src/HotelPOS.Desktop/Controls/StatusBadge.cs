using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using HotelPOS.Contracts.Presentation;
using HotelPOS.Desktop.Converters;
using HotelPOS.Desktop.Modules.Common;

namespace HotelPOS.Desktop.Controls;

// One status badge for every status in the app: soft fill + tone text + icon + label (or the solid variant: tone fill,
// white text). Give it a <see cref="StatusDisplay"/> from StatusPresentation, or set Tone / Label / Icon / Fill by hand.
// Colours are resource references, so a badge follows a live light/dark switch. Status is never colour alone: the
// label is always shown and the icon unless ShowIcon is false.
public sealed class StatusBadge : Control
{
    public static readonly DependencyProperty DisplayProperty =
        DependencyProperty.Register(nameof(Display), typeof(StatusDisplay), typeof(StatusBadge),
            new PropertyMetadata(default(StatusDisplay), (d, e) => ((StatusBadge)d).OnDisplayChanged((StatusDisplay)e.NewValue)));

    public static readonly DependencyProperty ToneProperty =
        DependencyProperty.Register(nameof(Tone), typeof(StatusTone), typeof(StatusBadge),
            new PropertyMetadata(StatusTone.Neutral, (d, _) => ((StatusBadge)d).Refresh()));

    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(StatusBadge), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(string), typeof(StatusBadge), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty FillProperty =
        DependencyProperty.Register(nameof(Fill), typeof(StatusFill), typeof(StatusBadge),
            new PropertyMetadata(StatusFill.Soft, (d, _) => ((StatusBadge)d).Refresh()));

    public static readonly DependencyProperty ShowIconProperty =
        DependencyProperty.Register(nameof(ShowIcon), typeof(bool), typeof(StatusBadge), new PropertyMetadata(true));

    public static readonly DependencyProperty BadgeBackgroundProperty =
        DependencyProperty.Register(nameof(BadgeBackground), typeof(Brush), typeof(StatusBadge), new PropertyMetadata(null));

    // The tone's strong colour: the 6 px dot and the hairline border (Phase 25 badge).
    public static readonly DependencyProperty BadgeAccentProperty =
        DependencyProperty.Register(nameof(BadgeAccent), typeof(Brush), typeof(StatusBadge), new PropertyMetadata(null));

    // On the navy sidebar a tinted fill looks like a sticker: OnDark draws the badge as an outline in the tone's strong colour.
    public static readonly DependencyProperty OnDarkProperty =
        DependencyProperty.Register(nameof(OnDark), typeof(bool), typeof(StatusBadge),
            new PropertyMetadata(false, (d, _) => ((StatusBadge)d).Refresh()));

    static StatusBadge()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(StatusBadge), new FrameworkPropertyMetadata(typeof(StatusBadge)));
        FocusableProperty.OverrideMetadata(typeof(StatusBadge), new FrameworkPropertyMetadata(false));
        IsTabStopProperty.OverrideMetadata(typeof(StatusBadge), new FrameworkPropertyMetadata(false));
    }

    public StatusBadge()
    {
        Refresh();
    }

    public StatusDisplay Display
    {
        get => (StatusDisplay)GetValue(DisplayProperty);
        set => SetValue(DisplayProperty, value);
    }

    public StatusTone Tone
    {
        get => (StatusTone)GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string Icon
    {
        get => (string)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public StatusFill Fill
    {
        get => (StatusFill)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public bool ShowIcon
    {
        get => (bool)GetValue(ShowIconProperty);
        set => SetValue(ShowIconProperty, value);
    }

    public Brush? BadgeAccent
    {
        get => (Brush?)GetValue(BadgeAccentProperty);
        set => SetValue(BadgeAccentProperty, value);
    }

    public bool OnDark
    {
        get => (bool)GetValue(OnDarkProperty);
        set => SetValue(OnDarkProperty, value);
    }

    public Brush? BadgeBackground
    {
        get => (Brush?)GetValue(BadgeBackgroundProperty);
        set => SetValue(BadgeBackgroundProperty, value);
    }

    private void OnDisplayChanged(StatusDisplay display)
    {
        Tone = display.Tone;
        Label = display.Label ?? string.Empty;
        Icon = display.Icon ?? string.Empty;
        Fill = display.Fill;
        Refresh();
    }

    private void Refresh()
    {
        var accent = StatusLabels.SolidBrushKey(Tone);
        SetResourceReference(BadgeAccentProperty, accent);

        if (OnDark && Fill != StatusFill.Solid)
        {
            // Outline on a dark surface: no fill, the tone's strong colour for border, dot and word.
            SetValue(BadgeBackgroundProperty, Brushes.Transparent);
            SetResourceReference(ForegroundProperty, accent);
            return;
        }

        var (background, foreground) = ToneBrushes.For(Tone, Fill);
        SetResourceReference(BadgeBackgroundProperty, background);
        if (foreground is null)
        {
            SetResourceReference(ForegroundProperty, "Brush.PrimaryText");
        }
        else
        {
            SetResourceReference(ForegroundProperty, foreground);
        }
    }
}

// A DataGrid column that shows a StatusDisplay as a StatusBadge: <controls:StatusBadgeColumn Header="Status" Binding="{Binding Display}" />.
// Rows keep the grid's 44 px height; the badge sits vertically centred and left aligned.
public sealed class StatusBadgeColumn : DataGridBoundColumn
{
    protected override FrameworkElement GenerateElement(DataGridCell cell, object dataItem) => Build();

    protected override FrameworkElement GenerateEditingElement(DataGridCell cell, object dataItem) => Build();

    private StatusBadge Build()
    {
        var badge = new StatusBadge { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        if (Binding is not null)
        {
            BindingOperations.SetBinding(badge, StatusBadge.DisplayProperty, Binding);
        }

        return badge;
    }
}
