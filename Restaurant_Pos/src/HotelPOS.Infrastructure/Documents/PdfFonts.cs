using System.Globalization;
using QuestPDF.Drawing;
using QuestPDF.Infrastructure;

namespace HotelPOS.Infrastructure.Documents;

// QuestPDF set-up, done once per process before the first document.
internal static class PdfFonts
{
    // Inter for Latin text; Noto Sans Devanagari and Noto Sans Tamil for regional item and restaurant names (all SIL
    // Open Font License 1.1, the licence texts sit beside the font files). Text is shaped by QuestPDF, so conjuncts
    // render correctly.
    public static readonly string[] Families = { "Inter", "Noto Sans Devanagari", "Noto Sans Tamil" };

    private static readonly object Gate = new();
    private static bool _configured;

    public static void EnsureConfigured()
    {
        lock (Gate)
        {
            if (_configured)
            {
                return;
            }

            // LICENCE DECISION PENDING (docs/phases/phase-14 § 2): QuestPDF's Community licence is free for
            // organisations below USD 1 M annual gross revenue. Confirm the owner qualifies, or buy a commercial
            // licence, before go-live; this is the only place the licence is declared.
            QuestPDF.Settings.License = LicenseType.Community;

            var assembly = typeof(PdfFonts).Assembly;
            foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".ttf", true, CultureInfo.InvariantCulture)))
            {
                using var stream = assembly.GetManifestResourceStream(resource)!;
                FontManager.RegisterFontFromStream(stream);
            }

            _configured = true;
        }
    }
}

// Amounts with Indian digit grouping (12,34,567.00), independent of the server's culture.
internal static class IndianNumbers
{
    private static readonly NumberFormatInfo Format = CreateFormat();

    public static string Amount(decimal value) => value.ToString("N2", Format);

    public static string Integer(long value) => value.ToString("N0", Format);

    public static string Money(decimal value, string? symbol) =>
        string.IsNullOrWhiteSpace(symbol) ? Amount(value) : symbol.Trim() + " " + Amount(value);

    private static NumberFormatInfo CreateFormat()
    {
        var format = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
        format.NumberGroupSizes = new[] { 3, 2 };
        return format;
    }
}
