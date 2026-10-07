using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using HotelPOS.Contracts.Print;

namespace HotelPOS.Desktop.Services.Printing;

// Same fixed-width lines as the thermal output, typeset for a Windows printer or the on-screen preview.
public sealed class FlowDocumentRenderer
{
    private const double FontSize = 12;

    public FlowDocument Render(object document, PrinterProfile profile)
    {
        var columns = EscPosRenderer.ColumnsFor(profile.PaperWidthMm);
        var lines = EscPosRenderer.Layout(document, columns, profile.CurrencyFallback);
        return Render(lines, columns);
    }

    public static FlowDocument Render(IReadOnlyList<PrintedLine> lines, int columns)
    {
        var characterWidth = FontSize * 0.6;
        var flow = new FlowDocument
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = FontSize,
            PagePadding = new Thickness(16),
            PageWidth = columns * characterWidth + 40,
            ColumnWidth = double.PositiveInfinity,
            Background = Brushes.White,
            Foreground = Brushes.Black,
        };

        foreach (var line in lines)
        {
            var run = new Run(line.Text.Length == 0 ? " " : line.Text);
            var paragraph = new Paragraph(run)
            {
                Margin = new Thickness(0),
                TextAlignment = line.Is(LineStyle.Center) ? TextAlignment.Center : line.Is(LineStyle.Right) ? TextAlignment.Right : TextAlignment.Left,
                FontWeight = line.Is(LineStyle.Bold) ? FontWeights.Bold : FontWeights.Normal,
                FontSize = line.Is(LineStyle.DoubleHeight) || line.Is(LineStyle.DoubleWidth) ? FontSize * 1.6 : FontSize,
            };
            flow.Blocks.Add(paragraph);
        }

        return flow;
    }
}
