using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Features.Menu.Commands;
using HotelPOS.Application.Features.Menu.Queries;
using HotelPOS.Application.Tests.Support;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Tests.Menu;

public class MenuPortionTests : DatabaseTestBase
{
    public MenuPortionTests(DatabaseFixture fixture)
        : base(fixture)
    {
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        CurrentUser.SignIn(await AdminIdAsync(), "admin", Roles.Admin);
    }

    private sealed record Setup(int CategoryId, int StationId, int TaxId);

    private async Task<Setup> SetupAsync()
    {
        var category = await SendAsync(new CreateCategoryCommand(new CreateCategoryRequest { Name = "Rice", SortOrder = 1 }));
        var station = await SendAsync(new CreateStationCommand(new SaveStationRequest { Name = "Grill", Code = "GRILL", SortOrder = 2 }));
        var tax = await SendAsync(new CreateTaxCommand(new SaveTaxRequest { Name = "GST 5%", Code = "GST5", RatePercent = 5m }));
        return new Setup(category.Value.Id, station.Value.Id, tax.Value.Id);
    }

    private static CreateMenuItemRequest Create(Setup s, string name = "Chicken Biryani", IReadOnlyList<SaveMenuItemPortionRequest>? portions = null) => new()
    {
        CategoryId = s.CategoryId,
        Name = name,
        Price = 180m,
        TaxId = s.TaxId,
        PreparationStationId = s.StationId,
        Portions = portions,
    };

    private static UpdateMenuItemRequest Update(MenuItemDto item) => new()
    {
        CategoryId = item.CategoryId,
        Name = item.Name,
        Code = item.Code,
        Price = item.Price,
        TaxId = item.TaxId,
        PreparationStationId = item.PreparationStationId,
        SortOrder = item.SortOrder,
        IsAvailable = item.IsAvailable,
        IsActive = item.IsActive,
        ModifierGroupIds = item.ModifierGroupIds,
        RowVersion = item.RowVersion,
    };

    private static List<SaveMenuItemPortionRequest> Four() => new()
    {
        new() { Name = "Quarter", Quantity = 250, Unit = "GM", Price = 100m, SortOrder = 1 },
        new() { Name = "Half", Quantity = 500, Unit = "GM", Price = 180m, IsDefault = true, SortOrder = 2 },
        new() { Name = "Full", Price = 320m, SortOrder = 3 },
        new() { Name = "1 KG", Quantity = 1, Unit = "KG", Price = 550m, IsActive = false, SortOrder = 4 },
    };

    private async Task<MenuItemDto> CreateItemAsync(Setup s, IReadOnlyList<SaveMenuItemPortionRequest>? portions = null)
    {
        var result = await SendAsync(new CreateMenuItemCommand(Create(s, portions: portions)));
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        return result.Value;
    }

    [Fact]
    public async Task Create_WithoutPortions_MakesOneRegularPortionAtThePrice()
    {
        var item = await CreateItemAsync(await SetupAsync());

        var portion = item.Portions.Should().ContainSingle().Subject;
        (portion.Name, portion.Price, portion.IsDefault, portion.IsActive).Should().Be(("Regular", 180m, true, true));
        item.Price.Should().Be(180m);
    }

    [Fact]
    public async Task Create_WithFourPortions_SavesThemAndMirrorsTheDefaultPortionPrice()
    {
        var item = await CreateItemAsync(await SetupAsync(), Four());

        item.Portions.Select(p => (p.Name, p.Price, p.IsDefault, p.IsActive)).Should().Equal(
            ("Quarter", 100m, false, true), ("Half", 180m, true, true), ("Full", 320m, false, true), ("1 KG", 550m, false, false));
        item.Portions.Select(p => p.Id).Should().OnlyContain(id => id > 0);
        item.Portions[0].Quantity.Should().Be(250m);
        item.Portions[0].Unit.Should().Be("GM");
        item.Price.Should().Be(180m, "the item price is the default portion's price");
    }

    [Fact]
    public async Task Create_IgnoresThePriceWhenPortionsAreGiven()
    {
        var s = await SetupAsync();

        var result = await SendAsync(new CreateMenuItemCommand(Create(s, portions: Four()) with { Price = 999m }));

        result.Value.Price.Should().Be(180m);
    }

    [Fact]
    public async Task Update_WithoutPortions_KeepsThem()
    {
        var item = await CreateItemAsync(await SetupAsync(), Four());

        var result = await SendAsync(new UpdateMenuItemCommand(item.Id, Update(item) with { Description = "Dum cooked" }));

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        result.Value.Portions.Select(p => (p.Id, p.Name, p.Price)).Should().Equal(item.Portions.Select(p => (p.Id, p.Name, p.Price)));
    }

    [Fact]
    public async Task Update_OldClientPriceChange_ChangesTheDefaultPortionPrice()
    {
        var item = await CreateItemAsync(await SetupAsync(), Four());

        var result = await SendAsync(new UpdateMenuItemCommand(item.Id, Update(item) with { Price = 200m }));

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        result.Value.Price.Should().Be(200m);
        result.Value.Portions.Single(p => p.IsDefault).Price.Should().Be(200m);
        result.Value.Portions.Single(p => p.Name == "Quarter").Price.Should().Be(100m, "only the default portion follows the old Price field");
        (await QueryAsync(db => db.AuditLogs.CountAsync(a => a.Action == AuditActions.MenuItemPriceChanged))).Should().Be(1);
    }

    [Fact]
    public async Task Update_WithPortions_KeepsTheIdsOfExistingRows_AndAddsAndRemoves()
    {
        var item = await CreateItemAsync(await SetupAsync(), Four());
        var half = item.Portions.Single(p => p.Name == "Half");
        var quarter = item.Portions.Single(p => p.Name == "Quarter");
        var rows = new List<SaveMenuItemPortionRequest>
        {
            new() { Id = quarter.Id, Name = "Quarter", Price = 110m, SortOrder = 1 },
            new() { Id = half.Id, Name = "Half", Price = 200m, IsDefault = true, SortOrder = 2 },
            new() { Name = "Jumbo", Price = 900m, SortOrder = 3 },
        };

        var result = await SendAsync(new UpdateMenuItemCommand(item.Id, Update(item) with { Portions = rows, Price = 1m }));

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        result.Value.Portions.Select(p => p.Name).Should().Equal("Quarter", "Half", "Jumbo");
        result.Value.Portions.Single(p => p.Name == "Half").Id.Should().Be(half.Id);
        result.Value.Portions.Single(p => p.Name == "Quarter").Id.Should().Be(quarter.Id);
        result.Value.Price.Should().Be(200m, "Price follows the default portion, the request's Price is ignored");
        (await QueryAsync(db => db.MenuItemPortions.CountAsync(p => p.MenuItemId == item.Id))).Should().Be(3);
    }

    [Fact]
    public async Task Update_MovingTheDefaultToAnotherPortion_Works()
    {
        var item = await CreateItemAsync(await SetupAsync(), Four());
        var rows = item.Portions.Select(p => new SaveMenuItemPortionRequest
        {
            Id = p.Id, Name = p.Name, Quantity = p.Quantity, Unit = p.Unit, Price = p.Price, IsActive = p.IsActive, SortOrder = p.SortOrder,
            IsDefault = p.Name == "Full",
        }).ToList();

        var result = await SendAsync(new UpdateMenuItemCommand(item.Id, Update(item) with { Portions = rows }));

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        result.Value.Portions.Single(p => p.IsDefault).Name.Should().Be("Full");
        result.Value.Price.Should().Be(320m);
    }

    [Fact]
    public async Task Update_SwappingTwoPortionNamesInOneSave_Works()
    {
        var item = await CreateItemAsync(await SetupAsync(), Four());
        var half = item.Portions.Single(p => p.Name == "Half");
        var full = item.Portions.Single(p => p.Name == "Full");
        var rows = item.Portions.Select(p => new SaveMenuItemPortionRequest
        {
            Id = p.Id, Name = p.Id == half.Id ? "Full" : p.Id == full.Id ? "Half" : p.Name, Quantity = p.Quantity, Unit = p.Unit,
            Price = p.Price, IsActive = p.IsActive, SortOrder = p.SortOrder, IsDefault = p.IsDefault,
        }).ToList();

        var result = await SendAsync(new UpdateMenuItemCommand(item.Id, Update(item) with { Portions = rows }));

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        result.Value.Portions.Single(p => p.Id == half.Id).Name.Should().Be("Full");
    }

    [Fact]
    public async Task InvalidPortionLists_AreRejectedWithAMessage_AndNothingIsSaved()
    {
        var s = await SetupAsync();
        var item = await CreateItemAsync(s, Four());
        var cases = new (string Why, List<SaveMenuItemPortionRequest> Rows, string Message)[]
        {
            ("duplicate name", new() { new() { Name = "Half", Price = 1m, IsDefault = true }, new() { Name = "HALF", Price = 2m } }, "used more than once"),
            ("two defaults", new() { new() { Name = "A", Price = 1m, IsDefault = true }, new() { Name = "B", Price = 2m, IsDefault = true } }, "exactly one default"),
            ("no default", new() { new() { Name = "A", Price = 1m }, new() { Name = "B", Price = 2m } }, "exactly one default"),
            ("none on", new() { new() { Name = "A", Price = 1m, IsDefault = true, IsActive = false } }, "at least one portion must be on"),
            ("default off", new() { new() { Name = "A", Price = 1m, IsDefault = true, IsActive = false }, new() { Name = "B", Price = 2m } }, "default portion must be on"),
            ("negative price", new() { new() { Name = "A", Price = -1m, IsDefault = true } }, "price of a"),
            ("13 portions", Enumerable.Range(1, 13).Select(i => new SaveMenuItemPortionRequest { Name = "P" + i, Price = i, IsDefault = i == 1 }).ToList(), "at most 12"),
            ("empty list", new(), "at least one portion"),
            ("blank name", new() { new() { Name = "  ", Price = 1m, IsDefault = true } }, "name"),
            ("quantity with 4 decimals", new() { new() { Name = "A", Price = 1m, IsDefault = true, Quantity = 0.0004m, Unit = "KG" } }, "3 decimals"),
            ("same portion id twice", new() { new() { Id = item.Portions[0].Id, Name = "A", Price = 1m, IsDefault = true }, new() { Id = item.Portions[0].Id, Name = "B", Price = 2m } }, "more than once"),
        };

        foreach (var (why, rows, message) in cases)
        {
            var update = await SendAsync(new UpdateMenuItemCommand(item.Id, Update(item) with { Portions = rows }));
            var create = await SendAsync(new CreateMenuItemCommand(Create(s, "Other " + why, rows)));

            update.IsSuccess.Should().BeFalse(why);
            update.Error!.Code.Should().Be(ErrorCodes.ValidationError, why);
            update.Error.Message.ToLowerInvariant().Should().Contain(message, why);
            create.IsSuccess.Should().BeFalse(why);
        }

        var unchanged = (await SendAsync(new GetMenuItemQuery(item.Id))).Value;
        unchanged.Portions.Select(p => p.Name).Should().Equal("Quarter", "Half", "Full", "1 KG");
    }

    [Fact]
    public async Task APortionListWithANullRow_IsAValidationError_NotACrash()
    {
        var s = await SetupAsync();
        var item = await CreateItemAsync(s, Four());
        var rows = new List<SaveMenuItemPortionRequest> { null! };

        var update = await SendAsync(new UpdateMenuItemCommand(item.Id, Update(item) with { Portions = rows }));
        var create = await SendAsync(new CreateMenuItemCommand(Create(s, "Null row dish", rows)));

        update.IsSuccess.Should().BeFalse();
        update.Error!.Code.Should().Be(ErrorCodes.ValidationError);
        create.IsSuccess.Should().BeFalse();
        create.Error!.Code.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task Menu_ListsOnlyActivePortions_DefaultFirstThenBySortOrder()
    {
        var item = await CreateItemAsync(await SetupAsync(), Four());

        var menu = (await SendAsync(new GetMenuQuery(null))).Value;

        var entry = menu.Items.Single(i => i.Id == item.Id);
        entry.Portions.Select(p => p.Name).Should().Equal("Half", "Quarter", "Full");
        entry.Price.Should().Be(180m);
        entry.Portions.Should().NotContain(p => p.Name == "1 KG");
    }

    [Fact]
    public async Task AnItemWithoutPortionChanges_ShowsItsRegularPortionInTheMenu()
    {
        var item = await CreateItemAsync(await SetupAsync());

        var entry = (await SendAsync(new GetMenuQuery(null))).Value.Items.Single(i => i.Id == item.Id);

        entry.Portions.Should().ContainSingle().Which.Name.Should().Be("Regular");
    }

    [Fact]
    public async Task AnyPortionWrite_BumpsTheMenuVersion()
    {
        var s = await SetupAsync();
        var item = await CreateItemAsync(s, Four());
        var before = (await SendAsync(new GetMenuQuery(null))).Value.Version;

        var rows = item.Portions.Select(p => new SaveMenuItemPortionRequest
        {
            Id = p.Id, Name = p.Name, Quantity = p.Quantity, Unit = p.Unit, Price = p.Id == item.Portions[0].Id ? 105m : p.Price,
            IsActive = p.IsActive, SortOrder = p.SortOrder, IsDefault = p.IsDefault,
        }).ToList();
        await SendAsync(new UpdateMenuItemCommand(item.Id, Update(item) with { Portions = rows }));

        (await SendAsync(new GetMenuQuery(null))).Value.Version.Should().BeGreaterThan(before);
    }

    [Fact]
    public async Task Audit_RecordsEachPortionOldAndNewPrice()
    {
        var item = await CreateItemAsync(await SetupAsync(), Four());
        var rows = item.Portions.Select(p => new SaveMenuItemPortionRequest
        {
            Id = p.Id, Name = p.Name, Quantity = p.Quantity, Unit = p.Unit, IsActive = p.IsActive, SortOrder = p.SortOrder, IsDefault = p.IsDefault,
            Price = p.Name == "Full" ? 340m : p.Name == "Quarter" ? 105m : p.Price,
        }).ToList();

        await SendAsync(new UpdateMenuItemCommand(item.Id, Update(item) with { Portions = rows }));

        var audit = await QueryAsync(db => db.AuditLogs.SingleAsync(a => a.Action == AuditActions.MenuItemUpdated));
        audit.OldValues.Should().Contain("Quarter").And.Contain("100").And.Contain("Full").And.Contain("320");
        audit.NewValues.Should().Contain("Quarter").And.Contain("105").And.Contain("Full").And.Contain("340");
    }

    [Fact]
    public async Task AWaiter_CannotEditPortions()
    {
        var item = await CreateItemAsync(await SetupAsync(), Four());
        CurrentUser.SignIn(await AdminIdAsync(), "waiter1", Roles.Waiter);

        var denied = await SendAsync(new UpdateMenuItemCommand(item.Id, Update(item) with { Portions = Four() }));

        denied.IsSuccess.Should().BeFalse();
        denied.Error!.Code.Should().Be(AppErrors.Forbidden().Code);
        (await SendAsync(new GetMenuItemQuery(item.Id))).Value.Portions.Should().HaveCount(4).And.Subject.Select(p => p.Name).Should().Contain("Quarter");
    }
}
