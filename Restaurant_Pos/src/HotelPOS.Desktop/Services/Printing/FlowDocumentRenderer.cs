using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HotelPOS.Contracts.Print;

namespace HotelPOS.Desktop.Services.Printing;

// Same fixed-width lines as the thermal output, typeset for a Windows printer or the on-screen preview. A template
// for an A4 or A5 sheet widens the lines to the sheet; the logo and the UPI QR code show as pictures.
public sealed class FlowDocumentRenderer
{
    private const double FontSize = 12;

    public FlowDocument Render(object document, PrinterProfile profile)
    {
        var columns = EscPosRenderer.ColumnsFor(profile, EscPosRenderer.TemplateOf(document));
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
            if (line.ImagePng is { Length: > 0 } png && Picture(png, line.QrPayload is null ? Math.Min(220, columns * characterWidth) : 150) is { } picture)
            {
                flow.Blocks.Add(new BlockUIContainer(picture) { Margin = new Thickness(0, 4, 0, 4) });
                continue;
            }

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

    private static Image? Picture(byte[] png, double maxWidth)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = new MemoryStream(png);
            bitmap.EndInit();
            bitmap.Freeze();
            return new Image
            {
                Source = bitmap,
                Stretch = Stretch.Uniform,
                MaxWidth = maxWidth,
                MaxHeight = Math.Min(150, maxWidth),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException or ArgumentException or FileFormatException)
        {
            return null;
        }
    }
}
