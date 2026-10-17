using System.Windows;
using System.Windows.Media;

namespace HotelPOS.Desktop.Controls;

// Shapes for the dashboard charts, drawn with plain WPF geometry (no chart library: LiveChartsCore 2 only ships
// .NET Framework builds of its WPF dependencies). Angles are in degrees, clockwise from 12 o'clock.
public static class ChartGeometry
{
    // One segment of a ring of the given outer radius and thickness, centred on (radius, radius).
    public static Geometry RingSegment(double startAngle, double sweepAngle, double radius, double thickness)
    {
        var inner = radius - thickness;
        var center = new Point(radius, radius);
        if (sweepAngle >= 359.99)
        {
            var ring = new CombinedGeometry(GeometryCombineMode.Exclude,
                new EllipseGeometry(center, radius, radius), new EllipseGeometry(center, inner, inner));
            ring.Freeze();
            return ring;
        }

        var end = startAngle + sweepAngle;
        var large = sweepAngle > 180;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(PointOn(center, radius, startAngle), isFilled: true, isClosed: true);
            context.ArcTo(PointOn(center, radius, end), new Size(radius, radius), 0, large, SweepDirection.Clockwise, true, true);
            context.LineTo(PointOn(center, inner, end), true, true);
            context.ArcTo(PointOn(center, inner, startAngle), new Size(inner, inner), 0, large, SweepDirection.Counterclockwise, true, true);
        }

        geometry.Freeze();
        return geometry;
    }

    // A series as a line in a 100 x 100 box (y flipped, scaled between the series' own min and max), for a Path with
    // Stretch="Fill". A flat series is drawn along the middle.
    public static Geometry Sparkline(IReadOnlyList<double> values)
    {
        var min = values.Min();
        var max = values.Max();
        var range = max - min;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            for (var i = 0; i < values.Count; i++)
            {
                var x = values.Count == 1 ? 0 : 100.0 * i / (values.Count - 1);
                var y = range <= 0 ? 50 : 100 - 100 * (values[i] - min) / range;
                if (i == 0)
                {
                    context.BeginFigure(new Point(x, y), isFilled: false, isClosed: false);
                }
                else
                {
                    context.LineTo(new Point(x, y), true, true);
                }
            }
        }

        geometry.Freeze();
        return geometry;
    }

    private static Point PointOn(Point center, double radius, double angle)
    {
        var radians = (angle - 90) * Math.PI / 180;
        return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
    }
}
