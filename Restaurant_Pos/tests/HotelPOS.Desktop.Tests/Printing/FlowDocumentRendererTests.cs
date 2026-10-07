using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Documents;
using HotelPOS.Contracts.Print;
using HotelPOS.Desktop.Services.Printing;

namespace HotelPOS.Desktop.Tests.Printing;

public sealed class FlowDocumentRendererTests
{
    [Fact]
    public void Invoice_BecomesOneParagraphPerLine_WithBoldAndLargeStyles()
    {
        RunOnSta(() =>
        {
            var profile = new PrinterProfile { Kind = PrinterKind.Windows, PaperWidthMm = 80 };
            var lines = EscPosRenderer.Layout(EscPosRendererTests.Invoice(), 48, "Rs.");

            var document = new FlowDocumentRenderer().Render(EscPosRendererTests.Invoice(), profile);

            var paragraphs = document.Blocks.OfType<Paragraph>().ToList();
            paragraphs.Should().HaveCount(lines.Count);
            paragraphs.Select(p => ((Run)p.Inlines.First()).Text).Should().ContainInOrder("Hotel Saravana Bhavan", "TAX INVOICE", "Thank you! Visit again.");
            var total = paragraphs.Single(p => ((Run)p.Inlines.First()).Text.StartsWith("TOTAL", StringComparison.Ordinal));
            total.FontWeight.Should().Be(FontWeights.Bold);
            total.FontSize.Should().BeGreaterThan(document.FontSize);
            paragraphs.Single(p => ((Run)p.Inlines.First()).Text == "TAX INVOICE").TextAlignment.Should().Be(TextAlignment.Center);
            document.PageWidth.Should().BeGreaterThan(300).And.BeLessThan(420, "an 80 mm receipt is narrow");
        });
    }

    [Fact]
    public void Kot_RendersOnNarrowPaper()
    {
        RunOnSta(() =>
        {
            var document = new FlowDocumentRenderer().Render(EscPosRendererTests.Kot(), new PrinterProfile { Kind = PrinterKind.Windows, PaperWidthMm = 58 });

            document.Blocks.OfType<Paragraph>().Select(p => ((Run)p.Inlines.First()).Text).Should().Contain(new string('-', 32));
        });
    }

    private static void RunOnSta(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
