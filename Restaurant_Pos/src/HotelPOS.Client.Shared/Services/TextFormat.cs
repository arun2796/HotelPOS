using System.Globalization;

namespace HotelPOS.Client.Services;

public static class TextFormat
{
    /// <summary>"1 guest", "2 guests", "0 items". English-only, like the rest of the staff client.</summary>
    public static string Count(int count, string singular, string? plural = null) =>
        $"{count.ToString(CultureInfo.CurrentCulture)} {(count == 1 ? singular : plural ?? singular + "s")}";
}
