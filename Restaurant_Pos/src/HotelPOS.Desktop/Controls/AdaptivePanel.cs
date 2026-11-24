using System.Windows;
using System.Windows.Controls;

namespace HotelPOS.Desktop.Controls;

/// <summary>
/// A wrapping grid that fills the width it is given: as many columns as fit at <see cref="MinItemWidth"/>, every cell stretched
/// to share the width evenly, rows of <see cref="ItemHeight"/>. Used for menu cards and order cards, which looked tiny and left
/// the right side of the screen empty when laid out at a fixed width.
/// </summary>
public sealed class AdaptivePanel : Panel
{
    public static readonly DependencyProperty MinItemWidthProperty = DependencyProperty.Register(
        nameof(MinItemWidth), typeof(double), typeof(AdaptivePanel),
        new FrameworkPropertyMetadata(200d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty ItemHeightProperty = DependencyProperty.Register(
        nameof(ItemHeight), typeof(double), typeof(AdaptivePanel),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public double MinItemWidth
    {
        get => (double)GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    // NaN: every row is as tall as its tallest card.
    public double ItemHeight
    {
        get => (double)GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }

    private int Columns(double width) =>
        double.IsInfinity(width) ? 1 : Math.Max(1, (int)Math.Floor(width / Math.Max(1, MinItemWidth)));

    protected override Size MeasureOverride(Size availableSize)
    {
        var columns = Columns(availableSize.Width);
        var cellWidth = double.IsInfinity(availableSize.Width) ? MinItemWidth : availableSize.Width / columns;
        var rowHeights = new List<double>();
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            child.Measure(new Size(cellWidth, double.IsNaN(ItemHeight) ? double.PositiveInfinity : ItemHeight));
            var row = i / columns;
            if (rowHeights.Count <= row)
            {
                rowHeights.Add(0);
            }

            rowHeights[row] = Math.Max(rowHeights[row], double.IsNaN(ItemHeight) ? child.DesiredSize.Height : ItemHeight);
        }

        return new Size(double.IsInfinity(availableSize.Width) ? cellWidth : availableSize.Width, rowHeights.Sum());
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var columns = Columns(finalSize.Width);
        var cellWidth = finalSize.Width / columns;
        double y = 0;
        for (var start = 0; start < InternalChildren.Count; start += columns)
        {
            var rowHeight = double.IsNaN(ItemHeight)
                ? Enumerable.Range(start, Math.Min(columns, InternalChildren.Count - start)).Max(i => InternalChildren[i].DesiredSize.Height)
                : ItemHeight;
            for (var c = 0; c < columns && start + c < InternalChildren.Count; c++)
            {
                InternalChildren[start + c].Arrange(new Rect(c * cellWidth, y, cellWidth, rowHeight));
            }

            y += rowHeight;
        }

        return finalSize;
    }
}
