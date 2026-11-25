using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Features.Floor.Commands;
using HotelPOS.Application.Features.Menu.Commands;
using HotelPOS.Application.Features.Menu.Queries;
using HotelPOS.Application.Features.Orders.Commands;
using HotelPOS.Application.Features.Orders.Queries;
using HotelPOS.Application.Tests.Support;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Orders;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Tests.Orders;

public class PortionOrderTests : DatabaseTestBase
{
    public PortionOrderTests(DatabaseFixture fixture)
        : base(fixture)
    {
    }

    private sealed record Setup(int TableId, MenuItemDto Biryani, MenuItemDto Naan, MenuItemDto Lassi, int Waiter)
    {
        public int Portion(string name) => Biryani.Portions.Single(p => p.Name == name).Id;
    }

    private async Task<Setup> SetupAsync()
    {
        CurrentUser.SignIn(await AdminIdAsync(), "admin", Roles.Admin);
        var section = await SendAsync(new CreateSectionCommand(new CreateSectionRequest { Name = "Ground Floor", SortOrder = 1 }));
        var table = await SendAsync(new CreateTableCommand(new CreateTableRequest { Code = "T05", SectionId = section.Value.Id, Capacity = 4 }));
        var category = await SendAsync(new CreateCategoryCommand(new CreateCategoryRequest { Name = "Mains", SortOrder = 1 }));
        var tax = await SendAsync(new CreateTaxCommand(new SaveTaxRequest { Name = "GST 5%", Code = "GST5", RatePercent = 5m }));
        var station = await QueryAsync(db => db.PreparationStations.Select(p => p.Id).FirstAsync());

        async Task<MenuItemDto> ItemAsync(string name, decimal price, decimal packaging, IReadOnlyList<SaveMenuItemPortionRequest>? portions)
        {
            var result = await SendAsync(new CreateMenuItemCommand(new CreateMenuItemRequest
            {
                CategoryId = category.Value.Id, Name = name, Price = price, TaxId = tax.Value.Id, PreparationStationId = station,
                PackagingCharge = packaging, Portions = portions,
            }));
            result.IsSuccess.Should().BeTrue(result.Error?.Message);
            return result.Value;
        }

        var biryani = await ItemAsync("Chicken Biryani", 1m, 10m, new List<SaveMenuItemPortionRequest>
        {
            new() { Name = "Quarter", Quantity = 250, Unit = "GM", Price = 100m, SortOrder = 1 },
            new() { Name = "Half", Quantity = 500, Unit = "GM", Price = 180m, IsDefault = true, SortOrder = 2 },
            new() { Name = "Full", Price = 320m, SortOrder = 3 },
            new() { Name = "1 KG", Quantity = 1, Unit = "KG", Price = 550m, IsActive = false, SortOrder = 4 },
        });
        var naan = await ItemAsync("Butter Naan", 50m, 0m, null);
        var lassi = await ItemAsync("Lassi", 1m, 0m, new List<SaveMenuItemPortionRequest>
        {
            new() { Name = "Half", Price = 60m, IsDefault = true, SortOrder = 1 },
            new() { Name = "Full", Price = 100m, IsActive = false, SortOrder = 2 },
        });
        var waiter = await CreateUserAsync("waitera", "secret1", Roles.Waiter);
        CurrentUser.SignIn(waiter.Id, "waitera", Roles.Waiter);
        return new Setup(table.Value.Id, biryani, naan, lassi, waiter.Id);
    }

    private static OrderItemInput Line(MenuItemDto item, int quantity, int? portionId = null, bool parcel = false) =>
        new() { MenuItemId = item.Id, Quantity = quantity, PortionId = portionId, IsParcel = parcel };

    private Task<Result<OrderDetailDto>> TryCreateAsync(Setup s, bool submit, params OrderItemInput[] items) =>
        SendAsync(new CreateOrderCommand(new CreateOrderRequest { TableId = s.TableId, GuestCount = 2, Items = items, Submit = submit }));

    private async Task<OrderDetailDto> CreateAsync(Setup s, bool submit, params OrderItemInput[] items)
    {
        var result = await TryCreateAsync(s, submit, items);
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        return result.Value;
    }

    private async Task SaveBiryaniPortionsAsync(Setup s, Func<MenuItemPortionDto, SaveMenuItemPortionRequest> edit)
    {
        var item = (await SendAsAdminAsync(new GetMenuItemQuery(s.Biryani.Id))).Value;
        var result = await SendAsAdminAsync(new UpdateMenuItemCommand(item.Id, new UpdateMenuItemRequest
        {
            CategoryId = item.CategoryId, Name = item.Name, Price = item.Price, TaxId = item.TaxId, PreparationStationId = item.PreparationStationId,
            PackagingCharge = item.PackagingCharge, ModifierGroupIds = item.ModifierGroupIds, RowVersion = item.RowVersion,
            Portions = item.Portions.Select(edit).ToList(),
        }));
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
    }

    private static SaveMenuItemPortionRequest Same(MenuItemPortionDto p, decimal? price = null, bool? isDefault = null, bool? isActive = null) => new()
    {
        Id = p.Id, Name = p.Name, Quantity = p.Quantity, Unit = p.Unit, Price = price ?? p.Price,
        IsDefault = isDefault ?? p.IsDefault, IsActive = isActive ?? p.IsActive, SortOrder = p.SortOrder,
    };

    [Fact]
    public async Task Order_OfAPortion_FreezesNameQuantityUnitAndPrice()
    {
        var s = await SetupAsync();

        var order = await CreateAsync(s, submit: true, Line(s.Biryani, 2, s.Portion("Half")));

        var line = order.Items.Single();
        line.UnitPrice.Should().Be(180m);
        line.PortionId.Should().Be(s.Portion("Half"));
        (line.PortionName, line.PortionQuantity, line.PortionUnit).Should().Be(("Half", 500m, "GM"));
        line.ItemName.Should().Be("Chicken Biryani");
        line.DisplayName.Should().Be("Chicken Biryani (Half)");
        line.LineTotal.Should().Be(360m);
        order.ApproxSubtotal.Should().Be(360m);
    }

    [Fact]
    public async Task PriceEditAfterSend_DoesNotChangeTheSentLine()
    {
        var s = await SetupAsync();
        var order = await CreateAsync(s, submit: true, Line(s.Biryani, 2, s.Portion("Half")));

        await SaveBiryaniPortionsAsync(s, p => Same(p, price: p.Name == "Half" ? 999m : null));
        var reloaded = (await SendAsync(new GetOrderQuery(order.Id))).Value;

        var line = reloaded.Items.Single();
        line.UnitPrice.Should().Be(180m);
        line.PortionName.Should().Be("Half");
        reloaded.ApproxSubtotal.Should().Be(360m);
        (await SendAsAdminAsync(new GetMenuItemQuery(s.Biryani.Id))).Value.Price.Should().Be(999m, "the menu did change");
    }

    [Fact]
    public async Task TwoPortionsOfOneItem_AreTwoLines_WithTheirOwnPrices()
    {
        var s = await SetupAsync();

        var order = await CreateAsync(s, submit: true, Line(s.Biryani, 1, s.Portion("Half")), Line(s.Biryani, 2, s.Portion("Full")));

        order.Items.Select(i => (i.DisplayName, i.Quantity, i.UnitPrice)).Should().Equal(
            ("Chicken Biryani (Half)", 1, 180m), ("Chicken Biryani (Full)", 2, 320m));
        order.ApproxSubtotal.Should().Be(820m);
    }

    [Fact]
    public async Task NoPortionId_OnASinglePortionItem_UsesItAndStoresNoPortion()
    {
        var s = await SetupAsync();

        var order = await CreateAsync(s, submit: true, Line(s.Naan, 1));

        var line = order.Items.Single();
        (line.UnitPrice, line.PortionId, line.PortionName, line.DisplayName).Should().Be((50m, (int?)null, (string?)null, "Butter Naan"));
    }

    [Fact]
    public async Task NoPortionId_OnATwoActivePortionItem_IsRefused_NamingTheItem()
    {
        var s = await SetupAsync();

        var result = await TryCreateAsync(s, submit: false, Line(s.Biryani, 1));

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(ErrorCodes.ValidationError);
        result.Error.Message.Should().Contain("Chicken Biryani has several portions").And.Contain("Update the app");
        (await QueryAsync(db => db.Orders.CountAsync())).Should().Be(0);
    }

    [Fact]
    public async Task AnInactivePortion_IsRefused_NamingItemAndPortion()
    {
        var s = await SetupAsync();

        var result = await TryCreateAsync(s, submit: false, Line(s.Biryani, 1, s.Portion("1 KG")));

        result.Error!.Code.Should().Be(ErrorCodes.ValidationError);
        result.Error.Message.Should().Be("Chicken Biryani (1 KG) is no longer available. Choose another portion.");
    }

    [Fact]
    public async Task APortionOfAnotherItem_IsRefused()
    {
        var s = await SetupAsync();

        var result = await TryCreateAsync(s, submit: false, Line(s.Biryani, 1, s.Naan.Portions.Single().Id));

        result.Error!.Code.Should().Be(ErrorCodes.ValidationError);
        result.Error.Message.Should().Contain("not offered with Chicken Biryani");
    }

    [Fact]
    public async Task ADeletedPortion_IsRefused_NotSwitched()
    {
        var s = await SetupAsync();
        var quarter = s.Portion("Quarter");
        await SaveBiryaniPortionsAsync(s, p => Same(p));
        var item = (await SendAsAdminAsync(new GetMenuItemQuery(s.Biryani.Id))).Value;
        await SendAsAdminAsync(new UpdateMenuItemCommand(item.Id, new UpdateMenuItemRequest
        {
            CategoryId = item.CategoryId, Name = item.Name, Price = item.Price, TaxId = item.TaxId, PreparationStationId = item.PreparationStationId,
            PackagingCharge = item.PackagingCharge, ModifierGroupIds = item.ModifierGroupIds, RowVersion = item.RowVersion,
            Portions = item.Portions.Where(p => p.Id != quarter).Select(p => Same(p)).ToList(),
        }));

        var result = await TryCreateAsync(s, submit: false, Line(s.Biryani, 1, quarter));

        result.Error!.Code.Should().Be(ErrorCodes.ValidationError);
        result.Error.Message.Should().Contain("Chicken Biryani");
    }

    [Fact]
    public async Task DraftResave_Reprices_FromTheCurrentPortion()
    {
        var s = await SetupAsync();
        var draft = await CreateAsync(s, submit: false, Line(s.Biryani, 1, s.Portion("Half")));
        draft.Items.Single().UnitPrice.Should().Be(180m);
        await SaveBiryaniPortionsAsync(s, p => Same(p, price: p.Name == "Half" ? 200m : null));

        var resaved = await SendAsync(new ReplaceOrderItemsCommand(draft.Id, new ReplaceOrderItemsRequest
        {
            Items = new[] { Line(s.Biryani, 1, s.Portion("Half")) },
            RowVersion = draft.RowVersion,
        }));

        resaved.IsSuccess.Should().BeTrue(resaved.Error?.Message);
        resaved.Value.Items.Single().UnitPrice.Should().Be(200m, "a draft is not a snapshot until it is sent");
    }

    [Fact]
    public async Task DraftResave_WithADisabledPortion_IsRefused_AndTheDraftIsUnchanged()
    {
        var s = await SetupAsync();
        var draft = await CreateAsync(s, submit: false, Line(s.Biryani, 1, s.Portion("Half")));
        await SaveBiryaniPortionsAsync(s, p => Same(p, isDefault: p.Name == "Full", isActive: p.Name == "Half" ? false : null));

        var resaved = await SendAsync(new ReplaceOrderItemsCommand(draft.Id, new ReplaceOrderItemsRequest
        {
            Items = new[] { Line(s.Biryani, 1, s.Portion("Half")) },
            RowVersion = draft.RowVersion,
        }));

        resaved.IsSuccess.Should().BeFalse();
        resaved.Error!.Message.Should().Be("Chicken Biryani (Half) is no longer available. Choose another portion.");
        var current = (await SendAsync(new GetOrderQuery(draft.Id))).Value;
        current.Items.Single().PortionName.Should().Be("Half");
        current.Items.Single().UnitPrice.Should().Be(180m, "nothing was switched to the default portion");
    }

    // A held draft can be sent without being saved again first (the web "edit draft" page does this after a restored local draft),
    // so the server must refuse a draft line whose portion is gone before the kitchen gets it.
    [Fact]
    public async Task SubmitOfAHeldDraft_WhosePortionWasSwitchedOff_IsRefused_AndStaysADraft()
    {
        var s = await SetupAsync();
        var draft = await CreateAsync(s, submit: false, Line(s.Biryani, 1, s.Portion("Half")));
        await SaveBiryaniPortionsAsync(s, p => Same(p, isDefault: p.Name == "Full", isActive: p.Name == "Half" ? false : null));

        var submitted = await SendAsync(new SubmitOrderCommand(draft.Id));

        submitted.IsSuccess.Should().BeFalse();
        submitted.Error!.Code.Should().Be(ErrorCodes.ValidationError);
        submitted.Error.Message.Should().Be("Chicken Biryani (Half) is no longer available. Choose another portion.");
        (await SendAsync(new GetOrderQuery(draft.Id))).Value.Status.Should().Be(OrderStatus.Draft);
        (await QueryAsync(db => db.KitchenOrders.CountAsync())).Should().Be(0, "nothing reached the kitchen");
    }

    [Fact]
    public async Task SubmitOfAHeldDraft_WhosePortionWasDeleted_IsRefused_NamingThePortion()
    {
        var s = await SetupAsync();
        var quarter = s.Portion("Quarter");
        var draft = await CreateAsync(s, submit: false, Line(s.Biryani, 1, quarter));
        var item = (await SendAsAdminAsync(new GetMenuItemQuery(s.Biryani.Id))).Value;
        (await SendAsAdminAsync(new UpdateMenuItemCommand(item.Id, new UpdateMenuItemRequest
        {
            CategoryId = item.CategoryId, Name = item.Name, Price = item.Price, TaxId = item.TaxId, PreparationStationId = item.PreparationStationId,
            PackagingCharge = item.PackagingCharge, ModifierGroupIds = item.ModifierGroupIds, RowVersion = item.RowVersion,
            Portions = item.Portions.Where(p => p.Id != quarter).Select(p => Same(p)).ToList(),
        }))).IsSuccess.Should().BeTrue();

        var submitted = await SendAsync(new SubmitOrderCommand(draft.Id));

        submitted.IsSuccess.Should().BeFalse();
        submitted.Error!.Message.Should().Be("Chicken Biryani (Quarter) is no longer available. Choose another portion.");
    }

    [Fact]
    public async Task SubmitOfAHeldDraft_WithItsPortionStillOnSale_Works()
    {
        var s = await SetupAsync();
        var draft = await CreateAsync(s, submit: false, Line(s.Biryani, 1, s.Portion("Half")), Line(s.Naan, 1));

        var submitted = await SendAsync(new SubmitOrderCommand(draft.Id));

        submitted.IsSuccess.Should().BeTrue(submitted.Error?.Message);
        submitted.Value.Status.Should().Be(OrderStatus.Submitted);
    }

    [Fact]
    public async Task TwoDefinedButOneActive_StillStoresThePortion_AndNeedsNoPortionId()
    {
        var s = await SetupAsync();

        var order = await CreateAsync(s, submit: true, Line(s.Lassi, 1));

        var line = order.Items.Single();
        (line.PortionName, line.UnitPrice, line.DisplayName).Should().Be(("Half", 60m, "Lassi (Half)"));
    }

    [Fact]
    public async Task AParcelOfAPortion_AddsPackingOncePerUnit_AndShowsBothInTheDisplayName()
    {
        var s = await SetupAsync();

        var order = await CreateAsync(s, submit: true, Line(s.Biryani, 2, s.Portion("Half"), parcel: true));

        var line = order.Items.Single();
        line.UnitPrice.Should().Be(190m, "180 + 10 packing");
        line.PackagingCharge.Should().Be(10m);
        line.ItemName.Should().Be("Chicken Biryani (Parcel)");
        line.DisplayName.Should().Be("Chicken Biryani (Half) (Parcel)");
        line.LineTotal.Should().Be(380m);
    }

    [Fact]
    public async Task Stock_TakesOnePerQuantity_ForAnyPortion()
    {
        var s = await SetupAsync();
        var stock = await SendAsAdminAsync(new SetMenuItemStockCommand(s.Biryani.Id, new SetStockRequest { Mode = StockTrackingMode.Exact, Quantity = 10 }));
        stock.IsSuccess.Should().BeTrue(stock.Error?.Message);

        await CreateAsync(s, submit: true, Line(s.Biryani, 2, s.Portion("Quarter")), Line(s.Biryani, 3, s.Portion("Full")));

        (await SendAsAdminAsync(new GetMenuItemQuery(s.Biryani.Id))).Value.StockRemaining.Should().Be(5, "10 - 2 - 3: a portion counts as 1 whatever its size");
    }

    [Fact]
    public async Task Append_OfAPortion_IsPricedAndFrozenLikeACreate()
    {
        var s = await SetupAsync();
        var order = await CreateAsync(s, submit: true, Line(s.Naan, 1));

        var appended = await SendAsync(new AppendOrderItemsCommand(order.Id, new AppendOrderItemsRequest { Items = new[] { Line(s.Biryani, 1, s.Portion("Full")) } }));

        appended.IsSuccess.Should().BeTrue(appended.Error?.Message);
        appended.Value.Items.Single(i => i.MenuItemId == s.Biryani.Id).DisplayName.Should().Be("Chicken Biryani (Full)");
        appended.Value.Items.Single(i => i.MenuItemId == s.Biryani.Id).UnitPrice.Should().Be(320m);
    }
}
