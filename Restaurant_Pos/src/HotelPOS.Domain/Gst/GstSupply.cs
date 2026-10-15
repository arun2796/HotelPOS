using HotelPOS.Contracts.Gst;

namespace HotelPOS.Domain.Gst;

public static class GstSupply
{
    public const decimal DefaultB2clThreshold = 100_000m;

    // Table 4 (registered buyer) wins; table 5 (B2CL) is an inter-state invoice to an unregistered buyer whose value is
    // above the threshold; everything else is B2CS. The threshold is a setting because the portal limit has changed.
    public static GstSupplyType Classify(bool buyerRegistered, bool interState, decimal invoiceValue, decimal b2clThreshold)
    {
        if (buyerRegistered)
        {
            return GstSupplyType.B2B;
        }

        return interState && invoiceValue > b2clThreshold ? GstSupplyType.B2CL : GstSupplyType.B2CS;
    }

    public static bool IsInterState(string? restaurantState, string? placeOfSupply) =>
        restaurantState is not null && placeOfSupply is not null && !string.Equals(restaurantState, placeOfSupply, StringComparison.Ordinal);
}

// One tax amount split into its heads: IGST for an inter-state supply, otherwise half CGST and half SGST. The central
// half is rounded and the state half takes the remainder, so the two always add up to the tax.
public readonly record struct GstSplit(decimal Igst, decimal Cgst, decimal Sgst)
{
    public static GstSplit Of(decimal tax, bool interState)
    {
        if (interState)
        {
            return new GstSplit(tax, 0m, 0m);
        }

        var central = decimal.Round(tax / 2, 2, MidpointRounding.AwayFromZero);
        return new GstSplit(0m, central, tax - central);
    }

    public decimal Total => Igst + Cgst + Sgst;

    public static GstSplit operator +(GstSplit a, GstSplit b) => new(a.Igst + b.Igst, a.Cgst + b.Cgst, a.Sgst + b.Sgst);
}
