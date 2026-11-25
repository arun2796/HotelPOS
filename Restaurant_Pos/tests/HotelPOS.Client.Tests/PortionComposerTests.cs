using FluentAssertions;
using HotelPOS.Client.Services;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Orders;
using Xunit;

namespace HotelPOS.Client.Tests;

/// <summary>Portions in the browser cart: priced from the chosen portion, kept apart, sent by id, restored from drafts and orders.</summary>
public class PortionComposerTests
{
    private static readonly MenuItemPortionDto Quarter = new() { Id = 1, Name = "Quarter", Quantity = 250, Unit = "GM", Price = 100, SortOrder = 1, IsActive = true };
    private static readonly MenuItemPortionDto Half = new() { Id = 2, Name = "Half", Quantity = 500, Unit = "GM", Price = 180, IsDefault = true, SortOrder = 2, IsActive = true };
    private static readonly MenuItemPortionDto Full = new() { Id = 3, Name = "Full", Price = 320, SortOrder = 3, IsActive = true };

    private static readonly MenuEntryDto Biryani = new()
    {
        Id = 5, Name = "Chicken Biryani", Price = 180, IsAvailable = true, PackagingCharge = 10,
        Portions = new[] { Half, Quarter, Full },
    };

    private static readonly MenuEntryDto Naan = new()
    {
        Id = 6, Name = "Butter Naan", Price = 50, IsAvailable = true,
        Portions = new[] { new MenuItemPortionDto { Id = 9, Name = "Regular", Price = 50, IsDefault = true, IsActive = true, SortOrder = 1 } },
    };

    private static readonly IReadOnlyList<MenuModifierOptionDto> None = Array.Empty<MenuModifierOptionDto>();

    [Fact]
    public void AddingAPortion_PricesTheLineAtThatPortion_AndNamesIt()
    {
        var cart = new OrderComposer();

        cart.Add(Biryani, None, 2, null, portion: Quarter);

        var line = cart.Lines.Single();
        line.UnitPrice.Should().Be(100);
        line.LineTotal.Should().Be(200);
        (line.PortionId, line.PortionName, line.PortionQuantity, line.PortionUnit).Should().Be((1, "Quarter", 250m, "GM"));
        line.Name.Should().Be("Chicken Biryani");
        line.DisplayName.Should().Be("Chicken Biryani (Quarter)");
    }

    [Fact]
    public void AddingWithoutAPortion_UsesTheDefaultPortion()
    {
        var cart = new OrderComposer();

        cart.Add(Biryani, None, 1, null);

        cart.Lines.Single().PortionId.Should().Be(2);
        cart.Lines.Single().UnitPrice.Should().Be(180);
    }

    [Fact]
    public void AnItemWithOnePortion_SendsItsPortionId_ButShowsNoPortionName()
    {
        var cart = new OrderComposer();

        cart.Add(Naan, None, 1, null);

        var line = cart.Lines.Single();
        line.PortionId.Should().Be(9);
        line.PortionName.Should().BeNull();
        line.DisplayName.Should().Be("Butter Naan");
        cart.ToInputs().Single().PortionId.Should().Be(9);
    }

    [Fact]
    public void AnItemFromAnOlderServer_WithNoPortions_StillAdds()
    {
        var cart = new OrderComposer();

        cart.Add(new MenuEntryDto { Id = 1, Name = "Masala Dosa", Price = 70, IsAvailable = true }, None, 1, null);

        var line = cart.Lines.Single();
        (line.UnitPrice, line.PortionId).Should().Be((70m, (int?)null));
    }

    [Fact]
    public void TheSameItemInTwoPortions_IsTwoLines_AndTheSamePortionMerges()
    {
        var cart = new OrderComposer();

        cart.Add(Biryani, None, 1, null, portion: Half);
        cart.Add(Biryani, None, 2, null, portion: Full);
        cart.Add(Biryani, None, 3, null, portion: Half);

        cart.Lines.Select(l => (l.DisplayName, l.Quantity)).Should().Equal(("Chicken Biryani (Half)", 4), ("Chicken Biryani (Full)", 2));
        cart.ApproxTotal.Should().Be(4 * 180 + 2 * 320);
    }

    [Fact]
    public void ToInputs_SendsThePortionId()
    {
        var cart = new OrderComposer();
        cart.Add(Biryani, None, 1, null, portion: Full);

        cart.ToInputs().Single().Should().BeEquivalentTo(new OrderItemInput { MenuItemId = 5, Quantity = 1, PortionId = 3 });
    }

    [Fact]
    public void AParcelOfAPortion_AddsPackingOnTop_AndKeepsThePortion()
    {
        var cart = new OrderComposer();
        cart.Add(Biryani, None, 2, null, isParcel: true, portion: Half);

        var line = cart.Lines.Single();
        line.LineTotal.Should().Be((180 + 10) * 2);
        line.DisplayName.Should().Be("Chicken Biryani (Half)");
    }

    [Fact]
    public void ADraft_RoundTripsThePortion()
    {
        var cart = new OrderComposer();
        cart.Add(Biryani, None, 2, null, portion: Quarter);

        var restored = new OrderComposer();
        restored.LoadDraft(cart.ToDraft(tableId: 7, orderId: null, pendingKey: null));

        var line = restored.Lines.Single();
        (line.PortionId, line.PortionName, line.PortionQuantity, line.PortionUnit, line.UnitPrice).Should().Be((1, "Quarter", 250m, "GM", 100m));
    }

    [Fact]
    public void AnOldDraftLine_WithoutPortionFields_Loads_AndNeedsAChoiceOnAMultiPortionItem()
    {
        var draft = new WebDraft
        {
            TableId = 7,
            Lines = new[] { new WebDraftLine { MenuItemId = 5, Name = "Chicken Biryani", UnitPrice = 180, Quantity = 1 }, new WebDraftLine { MenuItemId = 6, Name = "Butter Naan", UnitPrice = 50, Quantity = 1 } },
        };
        var cart = new OrderComposer();
        cart.LoadDraft(draft);

        cart.ResolvePortions(new MenuDto { Items = new[] { Biryani, Naan } });

        cart.Lines.Single(l => l.MenuItemId == 5).NeedsPortionChoice.Should().BeTrue("a line of an item with several portions is never silently given the default");
        cart.Lines.Single(l => l.MenuItemId == 6).NeedsPortionChoice.Should().BeFalse();
        cart.Lines.Single(l => l.MenuItemId == 6).PortionId.Should().Be(9, "an item with one portion needs no choice");
        cart.HasUnresolvedPortions.Should().BeTrue();
    }

    [Fact]
    public void ResolvePortions_MapsByIdThenByName_AndFlagsALineThatCannotBeMapped()
    {
        var cart = new OrderComposer();
        cart.LoadDraft(new WebDraft
        {
            TableId = 7,
            Lines = new[]
            {
                new WebDraftLine { MenuItemId = 5, Name = "Chicken Biryani", UnitPrice = 180, PortionId = 3, PortionName = "Full" },
                new WebDraftLine { MenuItemId = 5, Name = "Chicken Biryani", UnitPrice = 180, PortionId = 99, PortionName = "half", Notes = "a" },
                new WebDraftLine { MenuItemId = 5, Name = "Chicken Biryani", UnitPrice = 999, PortionId = 98, PortionName = "Jumbo", Notes = "b" },
            },
        });

        cart.ResolvePortions(new MenuDto { Items = new[] { Biryani } });

        cart.Lines.Select(l => (l.PortionId, l.NeedsPortionChoice)).Should().Equal(((int?)3, false), ((int?)2, false), ((int?)98, true));
    }

    [Fact]
    public void PickingAPortionAgain_ClearsTheFlag_AndKeepsTheOtherChoices()
    {
        var cart = new OrderComposer();
        cart.LoadDraft(new WebDraft { TableId = 7, Lines = new[] { new WebDraftLine { MenuItemId = 5, Name = "Chicken Biryani", UnitPrice = 999, PortionName = "Jumbo", Notes = "no onion" } } });
        cart.ResolvePortions(new MenuDto { Items = new[] { Biryani } });
        var line = cart.Lines.Single();

        cart.ChoosePortion(line, Biryani, Full);

        var fixedLine = cart.Lines.Single();
        (fixedLine.NeedsPortionChoice, fixedLine.PortionId, fixedLine.UnitPrice, fixedLine.Notes).Should().Be((false, 3, 320m, "no onion"));
        cart.HasUnresolvedPortions.Should().BeFalse();
    }

    [Fact]
    public void LoadOrder_RestoresTheFrozenPortionAndPrice_NeverTheCurrentOne()
    {
        var order = new OrderDetailDto
        {
            GuestCount = 2,
            Items = new[]
            {
                new OrderItemDto
                {
                    MenuItemId = 5, ItemName = "Chicken Biryani (Parcel)", UnitPrice = 190, IsParcel = true, PackagingCharge = 10, Quantity = 1,
                    PortionId = 2, PortionName = "Half", PortionQuantity = 500, PortionUnit = "GM", DisplayName = "Chicken Biryani (Half) (Parcel)", Status = OrderItemStatus.Draft,
                },
            },
        };
        var cart = new OrderComposer();

        cart.LoadOrder(order);

        var line = cart.Lines.Single();
        (line.Name, line.UnitPrice, line.PortionId, line.PortionName, line.IsParcel).Should().Be(("Chicken Biryani", 180m, 2, "Half", true));
        line.DisplayName.Should().Be("Chicken Biryani (Half)");
        line.LineTotal.Should().Be(190);
    }
}
