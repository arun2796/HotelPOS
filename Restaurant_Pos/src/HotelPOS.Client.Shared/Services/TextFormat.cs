using System.Globalization;

namespace HotelPOS.Client.Services;

public static class TextFormat
{
    /// <summary>"1 guest", "2 guests", "0 items". English-only, like the rest of the staff client.</summary>
    public static string Count(int count, string singular, string? plural = null) =>
        $"{count.ToString(CultureInfo.CurrentCulture)} {(count == 1 ? singular : plural ?? singular + "s")}";

    /// <summary>Placeholder letters for an item without a picture: "Chicken Biryani" is "CB", "Naan" is "N".</summary>
    public static string Initials(string? name)
    {
        var words = (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(w => char.IsLetterOrDigit(w[0])).ToList();
        return words.Count switch
        {
            0 => "?",
            1 => words[0][..1].ToUpperInvariant(),
            _ => (words[0][..1] + words[1][..1]).ToUpperInvariant(),
        };
    }
}
