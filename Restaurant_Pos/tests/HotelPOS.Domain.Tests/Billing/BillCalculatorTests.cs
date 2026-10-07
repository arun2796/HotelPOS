using HotelPOS.Contracts.Enums;
using HotelPOS.Domain.Billing;

namespace HotelPOS.Domain.Tests.Billing;

public class BillCalculatorTests
{
    [Fact]
    public void NoDiscount_AddsTaxPerLine_AndRoundsTheTotal()
    {
        var result = BillCalculator.Calculate(new[]
        {
            Line(1, 250m, 2, 5m),
            Line(2, 120m, 1, 5m),
        }, discount: null, roundOff: true);

        result.Subtotal.Should().Be(620m);
        result.DiscountAmount.Should().Be(0m);
        result.TaxableAmount.Should().Be(620m);
        result.TaxAmount.Should().Be(31m);
        result.RoundOff.Should().Be(0m);
        result.GrandTotal.Should().Be(651m);
        result.Lines.Select(l => l.LineTotal).Should().Equal(525m, 126m);
    }

    [Fact]
    public void PercentageDiscount_IsSharedByValue_AndTheLastLineTakesTheRoundingRemainder()
    {
        var result = BillCalculator.Calculate(new[]
        {
            Line(1, 33.33m, 1, 0m),
            Line(2, 33.33m, 1, 0m),
            Line(3, 33.34m, 1, 0m),
        }, new BillDiscountInput(DiscountType.Percentage, 10m), roundOff: false);

        result.DiscountAmount.Should().Be(10m);
        result.Lines.Select(l => l.DiscountShare).Should().Equal(3.33m, 3.33m, 3.34m);
        result.Lines.Sum(l => l.DiscountShare).Should().Be(result.DiscountAmount);
        result.GrandTotal.Should().Be(90m);
    }

    [Fact]
    public void Remainder_IsAbsorbedSoSharesAlwaysAddUp()
    {
        var result = BillCalculator.Calculate(new[]
        {
            Line(1, 10m, 1, 5m),
            Line(2, 10m, 1, 5m),
            Line(3, 10m, 1, 5m),
        }, new BillDiscountInput(DiscountType.FixedAmount, 10m), roundOff: false);

        result.Lines.Select(l => l.DiscountShare).Should().Equal(3.33m, 3.33m, 3.34m);
        result.TaxableAmount.Should().Be(20m);
        result.TaxAmount.Should().Be(0.99m, "tax is rounded per line: 0.33 + 0.33 + 0.33");
    }

    [Fact]
    public void FixedDiscountLargerThanTheSubtotal_IsCappedAtTheSubtotal()
    {
        var result = BillCalculator.Calculate(new[] { Line(1, 80m, 1, 5m) },
            new BillDiscountInput(DiscountType.FixedAmount, 500m), roundOff: true);

        result.DiscountAmount.Should().Be(80m);
        result.TaxableAmount.Should().Be(0m);
        result.TaxAmount.Should().Be(0m);
        result.GrandTotal.Should().Be(0m);
    }

    [Fact]
    public void MixedAndZeroTaxRates_AreTaxedPerLine_AndGroupedByRate()
    {
        var result = BillCalculator.Calculate(new[]
        {
            Line(1, 300m, 1, 5m),
            Line(2, 150m, 2, 18m),
            Line(3, 40m, 1, 0m),
            Line(4, 100m, 1, 5m),
        }, discount: null, roundOff: false);

        result.TaxAmount.Should().Be(15m + 54m + 0m + 5m);
        result.TaxGroups.Should().BeEquivalentTo(new[]
        {
            new TaxGroup(5m, 400m, 20m),
            new TaxGroup(18m, 300m, 54m),
        }, o => o.WithStrictOrdering());
        result.GrandTotal.Should().Be(814m);
    }

    [Theory]
    [InlineData(true, 0.19, 105.00)]
    [InlineData(false, 0.00, 104.81)]
    public void RoundOff_CanBeSwitchedOff(bool roundOff, decimal expectedRoundOff, decimal expectedTotal)
    {
        var result = BillCalculator.Calculate(new[] { Line(1, 99.82m, 1, 5m) }, discount: null, roundOff);

        result.TaxAmount.Should().Be(4.99m);
        result.RoundOff.Should().Be(expectedRoundOff);
        result.GrandTotal.Should().Be(expectedTotal);
    }

    [Fact]
    public void RoundOff_CanAlsoRoundDown()
    {
        var result = BillCalculator.Calculate(new[] { Line(1, 100.40m, 1, 0m) }, discount: null, roundOff: true);

        result.RoundOff.Should().Be(-0.40m);
        result.GrandTotal.Should().Be(100m);
    }

    [Fact]
    public void Modifiers_AreIncludedInTheLineSubtotal()
    {
        var result = BillCalculator.Calculate(new[] { new BillLineInput(1, "Biryani", 2, 250m, 30m, 5m) }, discount: null, roundOff: false);

        result.Lines.Single().LineSubtotal.Should().Be(560m);
        result.TaxAmount.Should().Be(28m);
    }

    [Fact]
    public void CancelledItems_AreLeftOut()
    {
        var result = BillCalculator.Calculate(new[]
        {
            Line(1, 200m, 1, 5m),
            new BillLineInput(2, "Kulfi", 1, 90m, 0m, 5m, IsCancelled: true),
        }, discount: null, roundOff: false);

        result.Lines.Should().ContainSingle();
        result.Subtotal.Should().Be(200m);
        result.GrandTotal.Should().Be(210m);
    }

    [Fact]
    public void PercentageDiscount_IsRoundedHalfAwayFromZero()
    {
        var result = BillCalculator.Calculate(new[] { Line(1, 0.25m, 1, 0m), Line(2, 0.20m, 1, 0m) },
            new BillDiscountInput(DiscountType.Percentage, 10m), roundOff: false);

        result.DiscountAmount.Should().Be(0.05m);
        result.Lines.Select(l => l.DiscountShare).Should().Equal(0.03m, 0.02m);
    }

    [Fact]
    public void ADiscountRemainder_SkipsTrailingFreeItems()
    {
        var result = BillCalculator.Calculate(new[]
        {
            Line(1, 10m, 1, 0m),
            Line(2, 10m, 1, 0m),
            Line(3, 10m, 1, 0m),
            Line(4, 0m, 1, 0m),
        }, new BillDiscountInput(DiscountType.FixedAmount, 10m), roundOff: false);

        result.Lines.Select(l => l.DiscountShare).Should().Equal(3.33m, 3.33m, 3.34m, 0m);
        result.Lines.Should().OnlyContain(l => l.TaxableAmount >= 0);
    }

    [Theory]
    [InlineData(DiscountType.Percentage, 15, 1000, 15)]
    [InlineData(DiscountType.FixedAmount, 150, 1000, 15)]
    [InlineData(DiscountType.FixedAmount, 50, 0, 0)]
    public void DiscountPercentOf_ComparesFixedAmountsWithTheSubtotal(DiscountType type, decimal value, decimal subtotal, decimal expected) =>
        BillCalculator.DiscountPercentOf(new BillDiscountInput(type, value), subtotal).Should().Be(expected);

    private static BillLineInput Line(int id, decimal price, int quantity, decimal taxRate) =>
        new(id, "Item " + id, quantity, price, 0m, taxRate);
}
