using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Infrastructure.Documents;
using UglyToad.PdfPig;

namespace HotelPOS.Application.Tests.Print;

// The bundled Noto Sans Tamil and Devanagari fonts (fallbacks after Inter) draw regional item names.
public class IndicFontTests
{
    [Fact]
    public void Pdf_WithTamilAndHindiItemNames_RendersWithoutMissingGlyphs()
    {
        QuestPDF.Settings.CheckIfAllTextGlyphsAreAvailable = true;
        var table = new ReportTable
        {
            Title = "Item sales",
            Columns = new[] { new ReportTableColumn("Item", ReportCellKind.Text, false), new ReportTableColumn("Amount", ReportCellKind.Money, true) },
            Rows = new IReadOnlyList<object?>[]
            {
                new object?[] { "சிக்கன் பிரியாணி Chicken Biryani", 250m },
                new object?[] { "पनीर बटर मसाला", 300m },
            },
            Totals = new object?[] { "Total", 550m },
            GeneratedAtLocal = new DateTime(2026, 10, 14, 9, 0, 0),
        };

        var bytes = new QuestPdfRenderer().Report(table, new Letterhead { RestaurantName = "சரவண பவன் Hotel" });

        using var document = PdfDocument.Open(bytes);
        document.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text).Should().Contain("Biryani").And.Contain("550.00");
    }
}
