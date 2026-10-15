using FluentAssertions;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Orders;
using HotelPOS.Web.Services;
using Xunit;

namespace HotelPOS.Web.Tests;

/// <summary>The browser order builder follows the same rules as the desktop one (docs/07 § 3.2.2).</summary>
public class OrderComposerTests
{
    private static readonly MenuEntryDto Dosa = new() { Id = 1, Name = "Masala Dosa", Price = 70, IsAvailable = true };
    private static readonly MenuEntryDto Biryani = new() { Id = 2, Name = "Chicken Biryani", Price = 180, IsAvailable = true, ModifierGroupIds = new[] { 10 } };
    private static readonly MenuModifierOptionDto Mild = new(101, "Mild", 0);
    private static readonly MenuModifierOptionDto Spicy = new(102, "Spicy", 0);
    private static readonly MenuModifierOptionDto ExtraRaita = new(103, "Extra raita", 20);

    [Fact]
    public void SameItemWithSameOptionsAndNote_MergesIntoOneLine_CappedAt99()
    {
        var cart = new OrderComposer();

        cart.Add(Dosa, Array.Empty<MenuModifierOptionDto>(), 2, null);
        cart.Add(Dosa, Array.Empty<MenuModifierOptionDto>(), 98, null);
        cart.Add(Dosa, Array.Empty<MenuModifierOptionDto>(), 1, "Less oil");

        cart.Lines.Should().HaveCount(2);
        cart.Lines[0].Quantity.Should().Be(OrderLimits.MaxQuantity);
        cart.Lines[1].Notes.Should().Be("Less oil");
        cart.ItemCount.Should().Be(100);
    }

    [Fact]
    public void LineTotal_IncludesOptionPrices_AndDecreaseRemovesAtZero()
    {
        var cart = new OrderComposer();
        cart.Add(Biryani, new[] { Spicy, ExtraRaita }, 2, null);

        var line = cart.Lines.Single();
        line.LineTotal.Should().Be((180 + 20) * 2);
        line.DetailText.Should().Be("Spicy, Extra raita");
        cart.ApproxTotal.Should().Be(400);

        cart.Decrease(line);
        cart.Decrease(line);
        cart.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void GuestsAndNotes_AreClampedLikeTheDesktop()
    {
        var cart = new OrderComposer { Guests = 150 };
        cart.Guests.Should().Be(99);
        cart.Guests = 0;
        cart.Guests.Should().Be(1);

        OrderComposer.TrimNotes("   ").Should().BeNull();
        OrderComposer.TrimNotes(new string('x', 600))!.Length.Should().Be(OrderLimits.NotesMaxLength);
    }

    [Theory]
    [InlineData(1, 1, "Choose 1")]
    [InlineData(0, 3, "Optional · up to 3")]
    [InlineData(1, 2, "Choose 1 to 2")]
    [InlineData(0, 1, "Optional")]
    public void GroupRuleText_MatchesTheDesktopWording(int min, int max, string expected)
    {
        var group = new MenuModifierGroupDto { Id = 10, Name = "Spice", MinSelections = min, MaxSelections = max, Options = new[] { Mild, Spicy } };

        OrderComposer.GroupRuleText(group).Should().Be(expected);
    }

    [Fact]
    public void AddIsRefused_UntilEveryGroupRuleIsMet()
    {
        var required = new MenuModifierGroupDto { Id = 10, Name = "Spice", MinSelections = 1, MaxSelections = 1, Options = new[] { Mild, Spicy } };
        var optional = new MenuModifierGroupDto { Id = 11, Name = "Extras", MinSelections = 0, MaxSelections = 2, Options = new[] { ExtraRaita } };
        var groups = new[] { required, optional };

        OrderComposer.AllGroupsSatisfied(groups, _ => 0).Should().BeFalse();
        OrderComposer.AllGroupsSatisfied(groups, g => g.Id == 10 ? 1 : 0).Should().BeTrue();
        OrderComposer.CanSelectMore(required, 1).Should().BeFalse();
        OrderComposer.CanSelectMore(optional, 1).Should().BeTrue();
        OrderComposer.GroupSatisfied(optional, 3).Should().BeFalse("more than the maximum is never valid");
    }

    [Fact]
    public void Draft_RoundTrips_TheWholeCart()
    {
        var cart = new OrderComposer { Guests = 4 };
        cart.Add(Biryani, new[] { Mild }, 3, "Parcel");
        var key = Guid.NewGuid();

        var draft = cart.ToDraft(tableId: 7, orderId: null, pendingKey: key);
        var restored = new OrderComposer();
        restored.LoadDraft(draft);

        draft.PendingKey.Should().Be(key);
        restored.Guests.Should().Be(4);
        restored.Lines.Single().Should().BeEquivalentTo(cart.Lines.Single(), o => o.Excluding(l => l.MergeKey).Excluding(l => l.DetailText).Excluding(l => l.LineTotal));
        restored.ToInputs().Single().Should().BeEquivalentTo(new OrderItemInput { MenuItemId = 2, Quantity = 3, Notes = "Parcel", ModifierOptionIds = new[] { 101 } });
    }
}
