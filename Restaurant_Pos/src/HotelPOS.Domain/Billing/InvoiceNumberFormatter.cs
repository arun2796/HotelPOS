using System.Globalization;
using HotelPOS.Domain.Common;

namespace HotelPOS.Domain.Billing;

public static class InvoiceNumberFormatter
{
    public const int MaxPrefixLength = 20;

    // The counter restarts per period. The number always carries year and month, so it stays unique whatever the policy.
    public static string PeriodKey(string? resetPolicy, DateTime localDate) => resetPolicy?.Trim().ToUpperInvariant() switch
    {
        "MONTHLY" => localDate.ToString("yyyyMM", CultureInfo.InvariantCulture),
        "NEVER" => "ALL",
        _ => localDate.ToString("yyyy", CultureInfo.InvariantCulture),
    };

    public static string Format(string? prefix, DateTime localDate, long sequence)
    {
        var cleanPrefix = prefix?.Trim() ?? string.Empty;
        if (cleanPrefix.Length > MaxPrefixLength)
        {
            throw new DomainException($"The invoice prefix may have at most {MaxPrefixLength} characters.");
        }

        if (sequence < 1)
        {
            throw new DomainException("Invoice numbers start at 1.");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{cleanPrefix}{localDate:yyyyMM}-{sequence:000000}");
    }
}
