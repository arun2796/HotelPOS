using System.Globalization;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Gst;

namespace HotelPOS.Application.Features.Operations;

// What a setting may hold beyond its data type (an Int is an Int, but a kitchen warning of -5 minutes or a GSTIN of
// "NOTAGSTIN" is still wrong). Returns the message to show, or null when the value is acceptable.
internal static class SettingRules
{
    private static readonly string[] ResetPolicies = { "Never", "Yearly", "Monthly" };
    private static readonly string[] TaxSplits = { "SINGLE", "CGST_SGST" };

    public static string? Check(string key, string value)
    {
        var v = value.Trim();
        return key switch
        {
            SettingKeys.RestaurantName => v.Length is 0 or > 100 ? "The restaurant name is required (up to 100 characters)." : null,
            SettingKeys.Address => v.Length > 300 ? "The address can have at most 300 characters." : null,
            SettingKeys.Phone => v.Length > 30 ? "The phone number can have at most 30 characters." : null,
            SettingKeys.Gstin => v.Length == 0 ? null : Gstin.Validate(v.ToUpperInvariant()),
            SettingKeys.CurrencySymbol => v.Length is 0 or > 5 ? "The currency symbol is 1 to 5 characters." : null,
            SettingKeys.InvoicePrefix => v.Length > 10 ? "The invoice prefix can have at most 10 characters." : null,
            SettingKeys.InvoiceResetPolicy => ResetPolicies.Contains(v, StringComparer.OrdinalIgnoreCase) ? null : "Choose Never, Yearly or Monthly.",
            SettingKeys.TaxSplitDisplay => TaxSplits.Contains(v, StringComparer.OrdinalIgnoreCase) ? null : "Choose SINGLE or CGST_SGST.",
            SettingKeys.KitchenWarnMinutes => Range(v, 1, 240, "Kitchen warning minutes"),
            SettingKeys.KitchenLateMinutes => Range(v, 1, 480, "Kitchen late minutes"),
            SettingKeys.PrintInvoiceCopies => Range(v, 1, 5, "Invoice copies"),
            SettingKeys.LockoutThreshold => Range(v, 1, 100, "Failed attempts before lockout"),
            SettingKeys.LockoutMinutes => Range(v, 1, 1440, "Lockout minutes"),
            SettingKeys.AuditRetentionDays => Range(v, 0, 3650, "Audit retention days"),
            SettingKeys.MaxCashierDiscountPercent => Range(v, 0, 100, "The cashier discount limit"),
            SettingKeys.ReceiptFooter => v.Length > 300 ? "The receipt footer can have at most 300 characters." : null,
            _ => null,
        };
    }

    // Numbers are already known to parse (the data type was checked first).
    private static string? Range(string value, decimal min, decimal max, string label)
    {
        var number = decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);
        return number < min || number > max ? $"{label} must be between {min:0.##} and {max:0.##}." : null;
    }
}
