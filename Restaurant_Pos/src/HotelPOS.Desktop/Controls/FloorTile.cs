using System.Windows;
using System.Windows.Media;

namespace HotelPOS.Desktop.Controls;

// Rotates a floor-plan tile about its centre. A bound RotateTransform inside a data template has no governing element
// when it is created, so the angle is applied through this attached property instead.
public static class FloorTile
{
    public static readonly DependencyProperty AngleProperty =
        DependencyProperty.RegisterAttached("Angle", typeof(double), typeof(FloorTile), new PropertyMetadata(0.0, OnAngleChanged));

    public static double GetAngle(DependencyObject element) => (double)element.GetValue(AngleProperty);

    public static void SetAngle(DependencyObject element, double value) => element.SetValue(AngleProperty, value);

    private static void OnAngleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement element)
        {
            element.RenderTransformOrigin = new Point(0.5, 0.5);
            element.RenderTransform = (double)e.NewValue == 0 ? Transform.Identity : new RotateTransform((double)e.NewValue);
        }
    }
}
