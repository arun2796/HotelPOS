using HotelPOS.Contracts.Billing;
using HotelPOS.Domain.Billing;

namespace HotelPOS.Application.Features.Billing;

// Shared by the bill screen and the printed invoice.
internal static class BillTaxBreakup
{
    public static IReadOnlyList<TaxBreakupDto> For(IEnumerable<BillItem> items, string? splitDisplay)
    {
        var groups = items
            .Where(i => i.TaxRatePercent > 0)
            .GroupBy(i => i.TaxRatePercent)
            .OrderBy(g => g.Key)
            .Select(g => (Rate: g.Key, Taxable: g.Sum(i => i.LineSubtotal - i.DiscountShare), Tax: g.Sum(i => i.TaxAmount)));
        var split = string.Equals(splitDisplay, "CGST_SGST", StringComparison.OrdinalIgnoreCase);
        var rows = new List<TaxBreakupDto>();
        foreach (var (rate, taxable, tax) in groups)
        {
            if (!split)
            {
                rows.Add(new TaxBreakupDto { Label = $"GST {rate:0.##}%", RatePercent = rate, TaxableAmount = taxable, TaxAmount = tax });
                continue;
            }

            var half = rate / 2;
            var central = BillCalculator.Money(tax / 2);
            rows.Add(new TaxBreakupDto { Label = $"CGST {half:0.##}%", RatePercent = half, TaxableAmount = taxable, TaxAmount = central });
            rows.Add(new TaxBreakupDto { Label = $"SGST {half:0.##}%", RatePercent = half, TaxableAmount = taxable, TaxAmount = tax - central });
        }

        return rows;
    }
}
