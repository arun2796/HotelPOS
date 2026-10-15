using HotelPOS.Contracts.Enums;

namespace HotelPOS.Domain.Billing;

public sealed record BillLineInput(
    int OrderItemId,
    string ItemName,
    int Quantity,
    decimal UnitPrice,
    decimal ModifiersAmount,
    decimal TaxRatePercent,
    bool IsCancelled = false);

public sealed record BillDiscountInput(DiscountType Type, decimal Value);

public sealed record BillLineResult(
    BillLineInput Line,
    decimal LineSubtotal,
    decimal DiscountShare,
    decimal TaxableAmount,
    decimal TaxAmount,
    decimal LineTotal);

public sealed record TaxGroup(decimal RatePercent, decimal TaxableAmount, decimal TaxAmount);

public sealed record BillCalculation(
    IReadOnlyList<BillLineResult> Lines,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal TaxableAmount,
    decimal TaxAmount,
    decimal RoundOff,
    decimal GrandTotal)
{
    public IReadOnlyList<TaxGroup> TaxGroups => Lines
        .Where(l => l.Line.TaxRatePercent > 0)
        .GroupBy(l => l.Line.TaxRatePercent)
        .OrderBy(g => g.Key)
        .Select(g => new TaxGroup(g.Key, g.Sum(l => l.TaxableAmount), g.Sum(l => l.TaxAmount)))
        .ToList();
}

// docs/02 § 6. Pure code: no EF or DI, so the same rules can run anywhere and the tests stay instant.
public static class BillCalculator
{
    public static BillCalculation Calculate(IReadOnlyList<BillLineInput> lines, BillDiscountInput? discount, bool roundOff)
    {
        var active = lines.Where(l => !l.IsCancelled).ToList();
        var subtotals = active.Select(l => Money((l.UnitPrice + l.ModifiersAmount) * l.Quantity)).ToList();
        var subtotal = subtotals.Sum();
        var discountAmount = DiscountAmount(discount, subtotal);

        var shares = new decimal[active.Count];
        if (discountAmount > 0)
        {
            for (var i = 0; i < active.Count; i++)
            {
                shares[i] = Money(discountAmount * subtotals[i] / subtotal);
            }

            // The rounding remainder goes to the last line that has a value, so no line is discounted below zero.
            var last = subtotals.FindLastIndex(s => s > 0);
            shares[last] += discountAmount - shares.Sum();
        }

        var results = new List<BillLineResult>(active.Count);
        for (var i = 0; i < active.Count; i++)
        {
            var taxable = subtotals[i] - shares[i];
            var tax = Money(taxable * active[i].TaxRatePercent / 100m);
            results.Add(new BillLineResult(active[i], subtotals[i], shares[i], taxable, tax, taxable + tax));
        }

        var taxableAmount = results.Sum(r => r.TaxableAmount);
        var taxAmount = results.Sum(r => r.TaxAmount);
        var raw = taxableAmount + taxAmount;
        var roundOffAmount = roundOff ? Math.Round(raw, 0, MidpointRounding.AwayFromZero) - raw : 0m;
        return new BillCalculation(results, subtotal, discountAmount, taxableAmount, taxAmount, roundOffAmount, raw + roundOffAmount);
    }

    public static decimal DiscountAmount(BillDiscountInput? discount, decimal subtotal)
    {
        if (discount is null || subtotal <= 0 || discount.Value <= 0)
        {
            return 0m;
        }

        return discount.Type switch
        {
            DiscountType.Percentage => Math.Min(Money(subtotal * discount.Value / 100m), subtotal),
            DiscountType.FixedAmount => Math.Min(discount.Value, subtotal),
            _ => 0m,
        };
    }

    public static decimal DiscountPercentOf(BillDiscountInput discount, decimal subtotal) => discount.Type switch
    {
        DiscountType.Percentage => discount.Value,
        _ when subtotal <= 0 => 0m,
        _ => discount.Value * 100m / subtotal,
    };

    public static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
