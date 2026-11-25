namespace HotelPOS.Contracts.Menu;

// Portion sizes an item can be sold in. A portion may carry a quantity with one of these units as a label
// ("Half · 500 GM"); it never changes a price.
public static class PortionUnits
{
    public static readonly IReadOnlyList<string> All = new[] { "GM", "KG", "ML", "L", "PCS", "PLATE" };

    // The canonical (upper-case) unit, or null when the text is not one of the list.
    public static string? Normalize(string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit))
        {
            return null;
        }

        var wanted = unit.Trim();
        return All.FirstOrDefault(u => string.Equals(u, wanted, StringComparison.OrdinalIgnoreCase));
    }
}

// The one place that turns a frozen item name and portion into the text staff read: "Chicken Biryani (Half)".
// The frozen name of a parcel line already ends with " (Parcel)", so the portion goes before it:
// "Chicken Biryani (Half) (Parcel)".
public static class ItemDisplayName
{
    public const string ParcelSuffix = " (Parcel)";

    public static string Compose(string itemName, string? portionName)
    {
        if (string.IsNullOrWhiteSpace(portionName))
        {
            return itemName;
        }

        var portion = " (" + portionName.Trim() + ")";
        return itemName.EndsWith(ParcelSuffix, StringComparison.Ordinal)
            ? itemName[..^ParcelSuffix.Length] + portion + ParcelSuffix
            : itemName + portion;
    }
}
