using System.Globalization;

namespace HotelPOS.Desktop.Modules.Common;

// "1 row", "2 rows": the count with the noun in the right number, for the few places that count things in text.
public static class Plural
{
    public static string Count(int count, string singular, string? plural = null) =>
        $"{count.ToString("N0", CultureInfo.CurrentCulture)} {(count == 1 ? singular : plural ?? singular + "s")}";

    // For a plural phrase received from the server ("Unpaid bills"): drops the trailing s of the last word when the
    // count is one.
    public static string CountPhrase(int count, string pluralPhrase)
    {
        if (count != 1 || !pluralPhrase.EndsWith('s') || pluralPhrase.EndsWith("ss", StringComparison.Ordinal))
        {
            return $"{count.ToString("N0", CultureInfo.CurrentCulture)} {pluralPhrase}";
        }

        return $"1 {pluralPhrase[..^1]}";
    }
}
