using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HotelPOS.Desktop.Controls;

// A stroke icon from Resources/Icons.xaml: <controls:Icon Kind="check" Width="16" Height="16" />. The Kind is the
// icon name StatusPresentation emits ("check", "flame", "wifi-off", ...); the colour is the control's Foreground, so
// an icon follows its text. An unknown Kind draws nothing.
public sealed class Icon : Control
{
    public static readonly DependencyProperty KindProperty =
        DependencyProperty.Register(nameof(Kind), typeof(string), typeof(Icon),
            new PropertyMetadata(string.Empty, (d, _) => ((Icon)d).Resolve()));

    private static readonly DependencyPropertyKey DataPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(Data), typeof(Geometry), typeof(Icon), new PropertyMetadata(null));

    public static readonly DependencyProperty DataProperty = DataPropertyKey.DependencyProperty;

    public static readonly DependencyProperty StrokeThicknessProperty =
        DependencyProperty.Register(nameof(StrokeThickness), typeof(double), typeof(Icon), new PropertyMetadata(1.75));

    static Icon()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(Icon), new FrameworkPropertyMetadata(typeof(Icon)));
        WidthProperty.OverrideMetadata(typeof(Icon), new FrameworkPropertyMetadata(16.0));
        HeightProperty.OverrideMetadata(typeof(Icon), new FrameworkPropertyMetadata(16.0));
        IsTabStopProperty.OverrideMetadata(typeof(Icon), new FrameworkPropertyMetadata(false));
        FocusableProperty.OverrideMetadata(typeof(Icon), new FrameworkPropertyMetadata(false));
    }

    public string Kind
    {
        get => (string)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public Geometry? Data => (Geometry?)GetValue(DataProperty);

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    // Looked up on every Kind change and again once the control is initialised (resources are reachable from there).
    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        Resolve();
    }

    private void Resolve()
    {
        var kind = Kind;
        var geometry = string.IsNullOrWhiteSpace(kind) ? null : TryFindResource("Icon." + kind) as Geometry;
        SetValue(DataPropertyKey, geometry);
    }
}
