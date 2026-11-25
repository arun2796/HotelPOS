using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Orders;
using HotelPOS.Desktop.Modules.Orders;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Orders;
using NSubstitute;

namespace HotelPOS.Desktop.Tests.Orders;

// Portions on the Windows order screen (captain / cashier): a tile of an item sold in several portions opens the picker with the
// Portion group first (default selected, price following the choice); the cart keeps one line per portion; the server gets the id.
public sealed partial class OrderBuilderViewModelTests
{
    private static readonly MenuItemPortionDto PHalf = new() { Id = 2, Name = "Half", Quantity = 500, Unit = "GM", Price = 180m, IsDefault = true, SortOrder = 2, IsActive = true };
    private static readonly MenuItemPortionDto PQuarter = new() { Id = 1, Name = "Quarter", Price = 100m, SortOrder = 1, IsActive = true };
    private static readonly MenuItemPortionDto PFull = new() { Id = 3, Name = "Full", Price = 320m, SortOrder = 3, IsActive = true };

    private static readonly MenuDto PortionedMenu = new()
    {
        Version = 4,
        Categories = new[] { new MenuCategoryDto(1, "Mains", 1) },
        Items = new[]
        {
            new MenuEntryDto { Id = 30, CategoryId = 1, Name = "Mutton Biryani", Price = 180m, IsAvailable = true, PackagingCharge = 10m, Portions = new[] { PHalf, PQuarter, PFull } },
            new MenuEntryDto
            {
                Id = 31, CategoryId = 1, Name = "Butter Naan", Price = 50m, IsAvailable = true,
                Portions = new[] { new MenuItemPortionDto { Id = 9, Name = "Regular", Price = 50m, IsDefault = true, IsActive = true, SortOrder = 1 } },
            },
            new MenuEntryDto { Id = 32, CategoryId = 1, Name = "Chicken Biryani", Price = 180m, IsAvailable = true, ModifierGroupIds = new[] { 1 }, Portions = new[] { PHalf, PQuarter } },
        },
        ModifierGroups = new[]
        {
            new MenuModifierGroupDto { Id = 1, Name = "Spice level", MinSelections = 1, MaxSelections = 1, Options = new[] { new MenuModifierOptionDto(101, "Mild", 0m) } },
        },
    };

    private Task<OrderBuilderViewModel> OpenPortionedAsync(OrderBuilderContext? context = null, IOrderSubmitter? submitter = null) =>
        OpenAsync(submitter, PortionedMenu, context);

    [Fact]
    public async Task ATileOfAMultiPortionItem_OpensThePickerWithTheDefaultPortionSelected()
    {
        var vm = await OpenPortionedAsync();

        vm.AddItemCommand.Execute(vm.Items.Single(i => i.Name == "Mutton Biryani"));

        vm.Picker.Should().NotBeNull();
        vm.Picker!.HasPortions.Should().BeTrue();
        vm.Picker.Portions.Select(p => (p.Portion.Name, p.IsSelected)).Should().Equal(("Quarter", false), ("Half", true), ("Full", false));
        vm.Picker.SelectedPortion!.Name.Should().Be("Half");
        vm.Picker.CurrentPrice.Should().Be(180m);
        vm.Cart.Should().BeEmpty("nothing is added until the picker is confirmed");
    }

    [Fact]
    public async Task PickingAPortion_PricesTheLineAndTheCartAtThatPortion()
    {
        var vm = await OpenPortionedAsync();
        vm.AddItemCommand.Execute(vm.Items.Single(i => i.Name == "Mutton Biryani"));

        vm.SelectPortionCommand.Execute(vm.Picker!.Portions.Single(p => p.Portion.Name == "Quarter"));
        vm.Picker.CurrentPrice.Should().Be(100m, "the price follows the chosen portion at once");
        vm.PickerQuantityCommand.Execute("1");
        vm.ConfirmPickerCommand.Execute(null);

        var line = vm.Cart.Should().ContainSingle().Subject;
        (line.UnitPrice, line.PortionId, line.PortionName, line.Quantity).Should().Be((100m, 1, "Quarter", 2));
        line.DisplayName.Should().Be("Mutton Biryani (Quarter)");
        line.LineTotal.Should().Be(200m);
        vm.Picker.Should().BeNull();
    }

    [Fact]
    public async Task TheSameItemInTwoPortions_IsTwoCartLines_AndTheSamePortionMerges()
    {
        var vm = await OpenPortionedAsync();
        var tile = vm.Items.Single(i => i.Name == "Mutton Biryani");

        void Add(string portion)
        {
            vm.AddItemCommand.Execute(tile);
            vm.SelectPortionCommand.Execute(vm.Picker!.Portions.Single(p => p.Portion.Name == portion));
            vm.ConfirmPickerCommand.Execute(null);
        }

        Add("Half");
        Add("Full");
        Add("Half");

        vm.Cart.Select(c => (c.DisplayName, c.Quantity)).Should().Equal(("Mutton Biryani (Half)", 2), ("Mutton Biryani (Full)", 1));
    }

    [Fact]
    public async Task AnItemWithOnePortion_AddsStraightAway_WithoutAPickerOrAPortionName()
    {
        var vm = await OpenPortionedAsync();

        vm.AddItemCommand.Execute(vm.Items.Single(i => i.Name == "Butter Naan"));

        vm.Picker.Should().BeNull();
        var line = vm.Cart.Should().ContainSingle().Subject;
        (line.PortionId, line.PortionName, line.DisplayName, line.UnitPrice).Should().Be((9, (string?)null, "Butter Naan", 50m));
    }

    [Fact]
    public async Task APortionedItemWithModifiers_ShowsBothGroupsInOnePicker()
    {
        var vm = await OpenPortionedAsync();

        vm.AddItemCommand.Execute(vm.Items.Single(i => i.Name == "Chicken Biryani"));
        vm.ConfirmPickerCommand.Execute(null);
        vm.Picker!.ErrorMessage.Should().Contain("Spice level", "the modifier rules still apply");
        vm.ToggleOptionCommand.Execute(vm.Picker.Groups[0].Options[0]);
        vm.SelectPortionCommand.Execute(vm.Picker.Portions.Single(p => p.Portion.Name == "Quarter"));
        vm.ConfirmPickerCommand.Execute(null);

        vm.Cart.Single().DisplayName.Should().Be("Chicken Biryani (Quarter)");
        vm.Cart.Single().ModifierNames.Should().Equal("Mild");
    }

    [Fact]
    public async Task Send_PostsThePortionId_AndAParcelKeepsItsPortion()
    {
        CreateOrderRequest? sent = null;
        _orders.CreateAsync(Arg.Do<CreateOrderRequest>(r => sent = r), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult<OrderDetailDto>.Ok(new OrderDetailDto { Id = 1, OrderNumber = 1001, Status = OrderStatus.Submitted }));
        var vm = await OpenPortionedAsync();
        vm.AddItemCommand.Execute(vm.Items.Single(i => i.Name == "Mutton Biryani"));
        vm.SelectPortionCommand.Execute(vm.Picker!.Portions.Single(p => p.Portion.Name == "Full"));
        vm.ConfirmPickerCommand.Execute(null);
        vm.ToggleParcelCommand.Execute(vm.Cart[0]);

        await vm.SendCommand.ExecuteAsync(null);

        sent!.Items.Single().Should().BeEquivalentTo(new OrderItemInput { MenuItemId = 30, Quantity = 1, PortionId = 3, IsParcel = true });
    }

    [Fact]
    public async Task ALocalDraft_RestoresItsPortion()
    {
        var first = await OpenPortionedAsync();
        first.AddItemCommand.Execute(first.Items.Single(i => i.Name == "Mutton Biryani"));
        first.SelectPortionCommand.Execute(first.Picker!.Portions.Single(p => p.Portion.Name == "Quarter"));
        first.ConfirmPickerCommand.Execute(null);
        first.OnNavigatedFrom();

        var reopened = await OpenPortionedAsync();

        var line = reopened.Cart.Should().ContainSingle().Subject;
        (line.PortionId, line.PortionName, line.PortionQuantity, line.UnitPrice, line.NeedsPortionChoice).Should().Be((1, "Quarter", (decimal?)null, 100m, false));
    }

    [Fact]
    public async Task ADraftLineWhosePortionIsGone_IsFlagged_AndSendingIsRefusedUntilItIsRemoved()
    {
        _drafts.Save(new LocalDraft
        {
            TableId = 5, TableCode = "T05", Mode = OrderBuilderMode.NewOrder, GuestCount = 2, SavedAtUtc = DateTime.UtcNow,
            Lines = new[] { new DraftLine { MenuItemId = 30, Name = "Mutton Biryani", UnitPrice = 999m, PortionId = 77, PortionName = "Jumbo", Quantity = 1 } },
        });
        var vm = await OpenPortionedAsync();

        vm.Cart.Single().NeedsPortionChoice.Should().BeTrue();

        await vm.SendCommand.ExecuteAsync(null);

        vm.ErrorMessage.Should().Contain("Mutton Biryani").And.Contain("portion");
        await _orders.DidNotReceiveWithAnyArgs().CreateAsync(default!, default, default);

        vm.RemoveCommand.Execute(vm.Cart[0]);
        vm.AddItemCommand.Execute(vm.Items.Single(i => i.Name == "Mutton Biryani"));
        vm.ConfirmPickerCommand.Execute(null);
        vm.Cart.Single().NeedsPortionChoice.Should().BeFalse();
    }

    [Fact]
    public async Task AnOldDraftLine_WithoutPortionData_OnAMultiPortionItem_IsNeverGivenTheDefaultSilently()
    {
        _drafts.Save(new LocalDraft
        {
            TableId = 5, TableCode = "T05", Mode = OrderBuilderMode.NewOrder, GuestCount = 2, SavedAtUtc = DateTime.UtcNow,
            Lines = new[]
            {
                new DraftLine { MenuItemId = 30, Name = "Mutton Biryani", UnitPrice = 180m, Quantity = 1 },
                new DraftLine { MenuItemId = 31, Name = "Butter Naan", UnitPrice = 50m, Quantity = 1 },
            },
        });

        var vm = await OpenPortionedAsync();

        vm.Cart.Single(c => c.MenuItemId == 30).NeedsPortionChoice.Should().BeTrue();
        vm.Cart.Single(c => c.MenuItemId == 31).NeedsPortionChoice.Should().BeFalse();
        vm.Cart.Single(c => c.MenuItemId == 31).PortionId.Should().Be(9, "an item with one portion needs no choice");
    }
}
