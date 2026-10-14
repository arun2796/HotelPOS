using System.Text.RegularExpressions;
using HotelPOS.Contracts.Gst;

namespace HotelPOS.Domain.Gst;

// GSTIN rules: 15 characters = state code (2 digits) + PAN (10) + entity number + "Z" + a check character computed
// over the first 14 (modulus 36, weights 1 and 2 alternating, as published by GSTN).
public static partial class Gstin
{
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    public const int Length = 15;

    public static string Normalize(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();

    // Null when the GSTIN is valid, otherwise what is wrong with it, in words a cashier can act on.
    public static string? Validate(string? value)
    {
        var gstin = Normalize(value);
        if (gstin.Length != Length)
        {
            return "A GSTIN has 15 characters.";
        }

        if (!FormatPattern().IsMatch(gstin))
        {
            return "That is not a GSTIN: expected 2 digits, 5 letters, 4 digits, a letter, a letter or digit, Z and a check character.";
        }

        if (!GstStates.IsKnown(gstin[..2]))
        {
            return $"State code {gstin[..2]} is not a GST state.";
        }

        return gstin[14] == CheckCharacter(gstin) ? null : "The GSTIN check character is wrong: check each character.";
    }

    public static bool IsValid(string? value) => Validate(value) is null;

    // The state code (first two digits) of a syntactically valid GSTIN; null otherwise.
    public static string? StateCode(string? value) => IsValid(value) ? Normalize(value)[..2] : null;

    public static char CheckCharacter(string first14OrFull)
    {
        var sum = 0;
        for (var i = 0; i < 14; i++)
        {
            var value = Alphabet.IndexOf(first14OrFull[i], StringComparison.Ordinal);
            var product = value * (i % 2 == 0 ? 1 : 2);
            sum += (product / 36) + (product % 36);
        }

        return Alphabet[(36 - (sum % 36)) % 36];
    }

    [GeneratedRegex("^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$")]
    private static partial Regex FormatPattern();
}

public static class GstStates
{
    private static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["01"] = "Jammu and Kashmir",
        ["02"] = "Himachal Pradesh",
        ["03"] = "Punjab",
        ["04"] = "Chandigarh",
        ["05"] = "Uttarakhand",
        ["06"] = "Haryana",
        ["07"] = "Delhi",
        ["08"] = "Rajasthan",
        ["09"] = "Uttar Pradesh",
        ["10"] = "Bihar",
        ["11"] = "Sikkim",
        ["12"] = "Arunachal Pradesh",
        ["13"] = "Nagaland",
        ["14"] = "Manipur",
        ["15"] = "Mizoram",
        ["16"] = "Tripura",
        ["17"] = "Meghalaya",
        ["18"] = "Assam",
        ["19"] = "West Bengal",
        ["20"] = "Jharkhand",
        ["21"] = "Odisha",
        ["22"] = "Chhattisgarh",
        ["23"] = "Madhya Pradesh",
        ["24"] = "Gujarat",
        ["26"] = "Dadra and Nagar Haveli and Daman and Diu",
        ["27"] = "Maharashtra",
        ["29"] = "Karnataka",
        ["30"] = "Goa",
        ["31"] = "Lakshadweep",
        ["32"] = "Kerala",
        ["33"] = "Tamil Nadu",
        ["34"] = "Puducherry",
        ["35"] = "Andaman and Nicobar Islands",
        ["36"] = "Telangana",
        ["37"] = "Andhra Pradesh",
        ["38"] = "Ladakh",
        ["97"] = "Other Territory",
        ["99"] = "Centre Jurisdiction",
    };

    public static IReadOnlyList<GstStateDto> All { get; } = Names.Select(n => new GstStateDto(n.Key, n.Value)).ToList();

    public static bool IsKnown(string? code) => code is not null && Names.ContainsKey(code);

    public static string NameOf(string? code) => code is not null && Names.TryGetValue(code, out var name) ? name : code ?? string.Empty;

    // "33-Tamil Nadu", the way the GST portal writes a place of supply.
    public static string Label(string? code) => code is null ? string.Empty : $"{code}-{NameOf(code)}";
}

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
