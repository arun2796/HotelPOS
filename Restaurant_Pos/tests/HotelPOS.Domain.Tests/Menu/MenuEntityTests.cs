using HotelPOS.Domain.Common;
using HotelPOS.Domain.Menu;

namespace HotelPOS.Domain.Tests.Menu;

public class MenuItemTests
{
    private static MenuItem NewItem(decimal price = 220m) =>
        new(categoryId: 1, " Chicken 65 ", " s01 ", "  ", price, taxId: 2, preparationStationId: 3, sortOrder: 1);

    [Fact]
    public void Constructor_TrimsAndNormalises_AndStartsAvailableAndActive()
    {
        var item = NewItem();

        item.Name.Should().Be("Chicken 65");
        item.Code.Should().Be("S01");
        item.Description.Should().BeNull();
        item.IsAvailable.Should().BeTrue();
        item.IsActive.Should().BeTrue();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10.555)]
    [InlineData(2_000_000)]
    public void Constructor_WithInvalidPrice_Throws(decimal price)
    {
        var act = () => NewItem(price);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void FreeItem_IsAllowed()
    {
        NewItem(0m).Price.Should().Be(0m);
    }

    [Fact]
    public void SetModifierGroups_KeepsTheGivenOrder_AndReplacesThePreviousLinks()
    {
        var item = NewItem();
        item.SetModifierGroups(new[] { 5, 7 });

        item.SetModifierGroups(new[] { 9, 5 });

        item.ModifierGroups.OrderBy(l => l.SortOrder).Select(l => l.ModifierGroupId).Should().Equal(9, 5);
    }

    [Fact]
    public void SetModifierGroups_WithDuplicates_Throws()
    {
        var act = () => NewItem().SetModifierGroups(new[] { 5, 5 });

        act.Should().Throw<DomainException>();
    }
}

public class ModifierAndTaxTests
{
    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(0, 3, true)]
    [InlineData(2, 1, false)]
    [InlineData(0, 0, false)]
    [InlineData(-1, 1, false)]
    public void ModifierGroup_SelectionRange_IsValidated(int min, int max, bool valid)
    {
        var act = () => new ModifierGroup("Spice level", min, max);

        if (valid)
        {
            act.Should().NotThrow();
        }
        else
        {
            act.Should().Throw<DomainException>();
        }
    }

    [Fact]
    public void ModifierOption_CanReduceThePrice()
    {
        new ModifierOption(1, "No cheese", -10m, 1).PriceDelta.Should().Be(-10m);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(100.01)]
    public void Tax_RateOutsideZeroToHundred_Throws(decimal rate)
    {
        var act = () => new Tax("Bad", "BAD", rate);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Tax_CodeIsUpperCased()
    {
        new Tax("GST 5%", " gst5 ", 5m).Code.Should().Be("GST5");
    }
}
