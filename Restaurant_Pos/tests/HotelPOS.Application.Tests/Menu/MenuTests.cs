using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Features.Menu.Commands;
using HotelPOS.Application.Features.Menu.Queries;
using HotelPOS.Application.Tests.Support;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using HotelPOS.Testing;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Tests.Menu;

public class MenuTests : DatabaseTestBase
{
    public MenuTests(DatabaseFixture fixture)
        : base(fixture)
    {
    }

    // The menu is maintained from the admin PC; taxes and stations are admin-only anyway.
    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        CurrentUser.SignIn(await AdminIdAsync(), "admin", Roles.Admin);
    }

    [Fact]
    public async Task CreateItem_WithANameAlreadyUsedInTheCategory_IsRejected_ButAllowedInAnother()
    {
        var setup = await SetupAsync();
        await CreateItemAsync(setup, "Chicken 65");
        var other = await SendAsync(new CreateCategoryCommand(new CreateCategoryRequest { Name = "Specials", SortOrder = 2 }));

        var duplicate = await SendAsync(new CreateMenuItemCommand(ItemRequest(setup, "chicken 65")));
        var elsewhere = await SendAsync(new CreateMenuItemCommand(ItemRequest(setup, "Chicken 65") with { CategoryId = other.Value.Id }));

        duplicate.Error!.Code.Should().Be(ErrorCodes.Duplicate);
        elsewhere.IsSuccess.Should().BeTrue(elsewhere.Error?.Message);
    }

    [Fact]
    public async Task UpdateItem_ChangingThePrice_WritesAPriceChangedAudit()
    {
        var setup = await SetupAsync();
        var item = await CreateItemAsync(setup, "Chicken 65");

        var result = await SendAsync(new UpdateMenuItemCommand(item.Id, UpdateRequest(item) with { Price = 235m }));

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        result.Value.Price.Should().Be(235m);
        var audit = await QueryAsync(db => db.AuditLogs.SingleAsync(a => a.Action == AuditActions.MenuItemPriceChanged));
        audit.EntityId.Should().Be(item.Id.ToString());
        audit.OldValues.Should().Contain("220");
        audit.NewValues.Should().Contain("235");
    }

    [Fact]
    public async Task UpdateItem_WithAStaleRowVersion_ReturnsTheCurrentItem()
    {
        var setup = await SetupAsync();
        var item = await CreateItemAsync(setup, "Chicken 65");
        await SendAsync(new UpdateMenuItemCommand(item.Id, UpdateRequest(item) with { Price = 230m }));

        var stale = await SendAsync(new UpdateMenuItemCommand(item.Id, UpdateRequest(item) with { Price = 240m }));

        stale.Error!.Code.Should().Be(ErrorCodes.ConcurrencyConflict);
        stale.Error.Data.Should().BeOfType<MenuItemDto>().Which.Price.Should().Be(230m);
    }

    [Fact]
    public async Task UpdateItem_ChangingOnlyModifierGroups_StillBumpsTheRowVersion()
    {
        var setup = await SetupAsync();
        var item = await CreateItemAsync(setup, "Chicken 65");

        var result = await SendAsync(new UpdateMenuItemCommand(item.Id, UpdateRequest(item) with { ModifierGroupIds = new[] { setup.GroupId } }));

        result.Value.ModifierGroupIds.Should().Equal(setup.GroupId);
        result.Value.RowVersion.Should().NotBe(item.RowVersion);
    }

    [Fact]
    public async Task DeactivatingAStationOrTaxInUse_IsRejected()
    {
        var setup = await SetupAsync();
        await CreateItemAsync(setup, "Chicken 65");

        var station = await SendAsync(new DeactivateStationCommand(setup.StationId));
        var tax = await SendAsync(new DeactivateTaxCommand(setup.TaxId));

        station.Error!.Code.Should().Be(ErrorCodes.BusinessRule);
        tax.Error!.Code.Should().Be(ErrorCodes.BusinessRule);
    }

    [Fact]
    public async Task DeactivatingACategoryWithItems_NeedsConfirmation_ThenDeactivatesTheItemsToo()
    {
        var setup = await SetupAsync();
        var item = await CreateItemAsync(setup, "Chicken 65");

        var refused = await SendAsync(new DeactivateCategoryCommand(setup.CategoryId, DeactivateItems: false));
        var confirmed = await SendAsync(new DeactivateCategoryCommand(setup.CategoryId, DeactivateItems: true));

        refused.Error!.Code.Should().Be(ErrorCodes.BusinessRule);
        confirmed.Value.IsActive.Should().BeFalse();
        (await QueryAsync(db => db.MenuItems.SingleAsync(i => i.Id == item.Id))).IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task EveryMenuWrite_BumpsTheVersion_AndPublishesMenuChangedOnce()
    {
        var before = (await SendAsync(new GetMenuQuery(null))).Value;
        var setup = await SetupAsync();
        var item = await CreateItemAsync(setup, "Chicken 65");
        Realtime.Reset();

        await SendAsync(new SetMenuItemAvailabilityCommand(item.Id, new SetAvailabilityRequest { IsAvailable = false }));
        var after = (await SendAsync(new GetMenuQuery(null))).Value;

        // Category, station, tax, modifier group + option, item: six writes during setup, one toggle.
        after.Version.Should().Be(before.Version + 7);
        var published = Realtime.Events.Should().ContainSingle().Subject;
        published.EventName.Should().Be(HubEvents.MenuChanged);
        published.Payload.Should().BeOfType<MenuChangedEvent>().Which.MenuVersion.Should().Be(after.Version);
    }

    [Fact]
    public async Task ConcurrentWrites_NeverLoseAVersionBump()
    {
        var setup = await SetupAsync();
        var start = (await SendAsync(new GetMenuQuery(null))).Value.Version;

        await Task.WhenAll(Enumerable.Range(1, 6).Select(i =>
            SendAsync(new CreateCategoryCommand(new CreateCategoryRequest { Name = $"Parallel {i}", SortOrder = i }))));

        (await SendAsync(new GetMenuQuery(null))).Value.Version.Should().Be(start + 6);
    }

    [Fact]
    public async Task GetMenu_LeavesOutInactiveData_MarksSoldOut_AndAnswersNotModifiedForTheCurrentVersion()
    {
        var setup = await SetupAsync();
        var kept = await CreateItemAsync(setup, "Chicken 65", withGroup: true);
        var removed = await CreateItemAsync(setup, "Old dish");
        await SendAsync(new DeactivateMenuItemCommand(removed.Id));
        await SendAsync(new SetMenuItemAvailabilityCommand(kept.Id, new SetAvailabilityRequest { IsAvailable = false }));

        var menu = (await SendAsync(new GetMenuQuery(null))).Value;
        var again = (await SendAsync(new GetMenuQuery(menu.Version))).Value;

        menu.NotModified.Should().BeFalse();
        var entry = menu.Items.Should().ContainSingle().Subject;
        entry.Name.Should().Be("Chicken 65");
        entry.IsAvailable.Should().BeFalse();
        entry.TaxRatePercent.Should().Be(5m);
        entry.ModifierGroupIds.Should().Equal(setup.GroupId);
        menu.ModifierGroups.Single().Options.Select(o => o.Name).Should().Equal("Mild", "Spicy");
        again.NotModified.Should().BeTrue();
        again.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task UploadImage_ResizesThePicture_AndRejectsNonImagesAndLargeFiles()
    {
        var setup = await SetupAsync();
        var item = await CreateItemAsync(setup, "Chicken 65");

        var stored = await UploadAsync(item.Id, TestImages.Png(1200, 800));
        var garbage = await UploadAsync(item.Id, TestImages.Garbage(1000));
        var large = await UploadAsync(item.Id, TestImages.Garbage(MenuLimits.MaxImageBytes + 1));

        stored.IsSuccess.Should().BeTrue(stored.Error?.Message);
        var file = Path.Combine(Fixture.MediaPath, "menu", Path.GetFileName(stored.Value.ImageUrl!));
        File.Exists(file).Should().BeTrue();
        using (var image = SkiaSharp.SKBitmap.Decode(file))
        {
            (image.Width, image.Height).Should().Be((512, 341));
        }

        garbage.Error!.Code.Should().Be(ErrorCodes.ValidationError);
        large.Error!.Code.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task ReplacingAPicture_DeletesThePreviousFile()
    {
        var setup = await SetupAsync();
        var item = await CreateItemAsync(setup, "Chicken 65");

        var first = await UploadAsync(item.Id, TestImages.Png(100, 100));
        var second = await UploadAsync(item.Id, TestImages.Png(200, 100));

        second.Value.ImageUrl.Should().NotBe(first.Value.ImageUrl);
        Directory.GetFiles(Path.Combine(Fixture.MediaPath, "menu"), $"{item.Id}-*").Should().ContainSingle()
            .Which.Should().EndWith(Path.GetFileName(second.Value.ImageUrl!));
    }

    private sealed record Setup(int CategoryId, int StationId, int TaxId, int GroupId);

    private async Task<Setup> SetupAsync()
    {
        var category = await SendAsync(new CreateCategoryCommand(new CreateCategoryRequest { Name = "Starters", SortOrder = 1 }));
        var station = await SendAsync(new CreateStationCommand(new SaveStationRequest { Name = "Grill", Code = "GRILL", SortOrder = 2 }));
        var tax = await SendAsync(new CreateTaxCommand(new SaveTaxRequest { Name = "GST 5%", Code = "GST5", RatePercent = 5m }));
        var group = await SendAsync(new CreateModifierGroupCommand(new SaveModifierGroupRequest { Name = "Spice level", MinSelections = 1, MaxSelections = 1 }));
        await SendAsync(new AddModifierOptionCommand(group.Value.Id, new SaveModifierOptionRequest { Name = "Spicy", SortOrder = 2 }));
        foreach (var result in new Result[] { category, station, tax, group })
        {
            result.IsSuccess.Should().BeTrue(result.Error?.Message);
        }

        // Added straight to the database so the version count in the version test stays simple.
        await QueryAsync(async db =>
        {
            db.ModifierOptions.Add(new Domain.Menu.ModifierOption(group.Value.Id, "Mild", 0m, 1));
            db.ModifierOptions.Add(new Domain.Menu.ModifierOption(group.Value.Id, "Retired", 0m, 3));
            await db.SaveChangesAsync();
            var retired = await db.ModifierOptions.SingleAsync(o => o.Name == "Retired");
            retired.Deactivate();
            return await db.SaveChangesAsync();
        });

        return new Setup(category.Value.Id, station.Value.Id, tax.Value.Id, group.Value.Id);
    }

    private static CreateMenuItemRequest ItemRequest(Setup setup, string name) => new()
    {
        CategoryId = setup.CategoryId,
        Name = name,
        Price = 220m,
        TaxId = setup.TaxId,
        PreparationStationId = setup.StationId,
    };

    private static UpdateMenuItemRequest UpdateRequest(MenuItemDto item) => new()
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

    private async Task<MenuItemDto> CreateItemAsync(Setup setup, string name, bool withGroup = false)
    {
        var request = ItemRequest(setup, name) with { ModifierGroupIds = withGroup ? new[] { setup.GroupId } : Array.Empty<int>() };
        var result = await SendAsync(new CreateMenuItemCommand(request));
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        return result.Value;
    }

    private async Task<Result<MenuItemDto>> UploadAsync(int itemId, byte[] content)
    {
        await using var stream = new MemoryStream(content);
        return await SendAsync(new UploadMenuItemImageCommand(itemId, stream, content.Length));
    }
}
