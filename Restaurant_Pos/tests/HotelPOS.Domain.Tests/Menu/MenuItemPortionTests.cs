using HotelPOS.Contracts.Menu;
using HotelPOS.Domain.Common;
using HotelPOS.Domain.Menu;

namespace HotelPOS.Domain.Tests.Menu;

public class MenuItemPortionTests
{
    private static MenuItem NewItem(decimal price = 180m) =>
        new(categoryId: 1, "Chicken Biryani", "CB", null, price, taxId: 2, preparationStationId: 3, sortOrder: 1);

    private static PortionSpec Spec(string name, decimal price, bool isDefault = false, bool isActive = true, int sort = 1, int? id = null, decimal? quantity = null, string? unit = null) =>
        new(id, name, quantity, unit, price, isDefault, isActive, sort);

    private static List<PortionSpec> Valid() => new()
    {
        Spec("Quarter", 100m, sort: 1, quantity: 250, unit: "GM"),
        Spec("Half", 180m, isDefault: true, sort: 2, quantity: 500, unit: "GM"),
        Spec("Full", 320m, sort: 3),
    };

    [Fact]
    public void NewItem_HasOneRegularDefaultPortionAtItsPrice()
    {
        var item = NewItem(220m);

        var portion = item.Portions.Should().ContainSingle().Subject;
        portion.Name.Should().Be("Regular");
        portion.Price.Should().Be(220m);
        portion.IsDefault.Should().BeTrue();
        portion.IsActive.Should().BeTrue();
    }

    [Fact]
    public void SetPortions_SetsTheItemPriceToTheDefaultPortionPrice()
    {
        var item = NewItem();

        item.SetPortions(Valid());

        item.Portions.Select(p => p.Name).Should().Equal("Quarter", "Half", "Full");
        item.Price.Should().Be(180m);
        item.Portions.Single(p => p.IsDefault).Name.Should().Be("Half");
    }

    [Fact]
    public void SetPortions_KeepsTheIdentityOfExistingRows()
    {
        var item = NewItem();
        var regular = item.Portions.Single();
        regular.Id = 77;

        item.SetPortions(new List<PortionSpec> { Spec("Regular", 200m, isDefault: true, id: 77), Spec("Large", 300m, sort: 2) });

        item.Portions.Single(p => p.Name == "Regular").Should().BeSameAs(regular);
        regular.Price.Should().Be(200m);
        item.Price.Should().Be(200m);
    }

    [Fact]
    public void SetPortions_RejectsDuplicateNamesIgnoringCase()
    {
        var specs = Valid();
        specs[2] = Spec("HALF", 320m, sort: 3);

        var act = () => NewItem().SetPortions(specs);

        act.Should().Throw<DomainException>().WithMessage("*Half*once*");
    }

    [Fact]
    public void SetPortions_RejectsTwoDefaults()
    {
        var specs = Valid();
        specs[2] = Spec("Full", 320m, isDefault: true, sort: 3);

        var act = () => NewItem().SetPortions(specs);

        act.Should().Throw<DomainException>().WithMessage("*one default*");
    }

    [Fact]
    public void SetPortions_RejectsNoDefault()
    {
        var specs = Valid();
        specs[1] = Spec("Half", 180m, sort: 2);

        var act = () => NewItem().SetPortions(specs);

        act.Should().Throw<DomainException>().WithMessage("*one default*");
    }

    [Fact]
    public void SetPortions_RejectsNoActivePortion()
    {
        var specs = new List<PortionSpec> { Spec("Half", 180m, isDefault: true, isActive: false) };

        var act = () => NewItem().SetPortions(specs);

        act.Should().Throw<DomainException>().WithMessage("*at least one*on*");
    }

    [Fact]
    public void SetPortions_RejectsAnInactiveDefault()
    {
        var specs = Valid();
        specs[1] = Spec("Half", 180m, isDefault: true, isActive: false, sort: 2);

        var act = () => NewItem().SetPortions(specs);

        act.Should().Throw<DomainException>().WithMessage("*default*on*");
    }

    [Fact]
    public void SetPortions_Rejects13Portions()
    {
        var specs = Enumerable.Range(1, MenuLimits.MaxPortions + 1).Select(i => Spec("P" + i, 10m * i, isDefault: i == 1, sort: i)).ToList();

        var act = () => NewItem().SetPortions(specs);

        act.Should().Throw<DomainException>().WithMessage("*at most 12*");
    }

    [Fact]
    public void SetPortions_Accepts12Portions()
    {
        var specs = Enumerable.Range(1, MenuLimits.MaxPortions).Select(i => Spec("P" + i, 10m * i, isDefault: i == 1, sort: i)).ToList();

        var item = NewItem();
        item.SetPortions(specs);

        item.Portions.Should().HaveCount(12);
    }

    [Fact]
    public void SetPortions_RejectsNegativeOrTooPreciseOrTooBigPrices()
    {
        foreach (var bad in new[] { -1m, 10.555m, MenuLimits.MaxPrice + 1 })
        {
            var act = () => NewItem().SetPortions(new List<PortionSpec> { Spec("Half", bad, isDefault: true) });
            act.Should().Throw<DomainException>($"{bad} is not a valid portion price");
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(100001)]
    public void SetPortions_RejectsAQuantityOutOfRange(double quantity)
    {
        var act = () => NewItem().SetPortions(new List<PortionSpec> { Spec("Half", 180m, isDefault: true, quantity: (decimal)quantity, unit: "GM") });

        act.Should().Throw<DomainException>().WithMessage("*quantity*");
    }

    [Fact]
    public void SetPortions_RejectsAnUnknownUnit_AndAcceptsTheFixedList()
    {
        var bad = () => NewItem().SetPortions(new List<PortionSpec> { Spec("Half", 180m, isDefault: true, quantity: 1, unit: "BARREL") });
        bad.Should().Throw<DomainException>().WithMessage("*unit*");

        foreach (var unit in PortionUnits.All)
        {
            var item = NewItem();
            item.SetPortions(new List<PortionSpec> { Spec("Half", 180m, isDefault: true, quantity: 1, unit: unit.ToLowerInvariant()) });
            item.Portions.Single().Unit.Should().Be(unit, "units are stored upper-case");
        }
    }

    [Fact]
    public void SetDefaultPrice_ChangesBothPrices()
    {
        var item = NewItem();
        item.SetPortions(Valid());

        item.SetDefaultPrice(200m);

        item.Price.Should().Be(200m);
        item.Portions.Single(p => p.IsDefault).Price.Should().Be(200m);
        item.Portions.Single(p => p.Name == "Quarter").Price.Should().Be(100m);
    }

    [Fact]
    public void ADisabledPortionIsKept_ButTheDefaultStaysActive()
    {
        var item = NewItem();
        var specs = Valid();
        specs[2] = Spec("Full", 320m, isActive: false, sort: 3);

        item.SetPortions(specs);

        item.Portions.Single(p => p.Name == "Full").IsActive.Should().BeFalse();
        item.Portions.Count(p => p.IsActive).Should().Be(2);
    }
}
