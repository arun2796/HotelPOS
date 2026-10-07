using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Menu;
using HotelPOS.Application.Tests.Support;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HotelPOS.Application.Tests.Menu;

public class MenuServiceTests : DatabaseTestBase
{
    public MenuServiceTests(DatabaseFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task CreateItem_WithANameAlreadyUsedInTheCategory_IsRejected_ButAllowedInAnother()
    {
        var setup = await SetupAsync();
        await CreateItemAsync(setup, "Chicken 65");
        var other = await Call<ICategoryService, CategoryDto>(s => s.CreateAsync(new CreateCategoryRequest { Name = "Specials", SortOrder = 2 }));

        var duplicate = await Call<IMenuItemService, MenuItemDto>(s => s.CreateAsync(ItemRequest(setup, "chicken 65")));
        var elsewhere = await Call<IMenuItemService, MenuItemDto>(s => s.CreateAsync(ItemRequest(setup, "Chicken 65") with { CategoryId = other.Value.Id }));

        duplicate.Error!.Code.Should().Be(ErrorCodes.Duplicate);
        elsewhere.IsSuccess.Should().BeTrue(elsewhere.Error?.Message);
    }

    [Fact]
    public async Task UpdateItem_ChangingThePrice_WritesAPriceChangedAudit()
    {
        var setup = await SetupAsync();
        var item = await CreateItemAsync(setup, "Chicken 65");

        var result = await Call<IMenuItemService, MenuItemDto>(s => s.UpdateAsync(item.Id, UpdateRequest(item) with { Price = 235m }));

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
        await Call<IMenuItemService, MenuItemDto>(s => s.UpdateAsync(item.Id, UpdateRequest(item) with { Price = 230m }));

        var stale = await Call<IMenuItemService, MenuItemDto>(s => s.UpdateAsync(item.Id, UpdateRequest(item) with { Price = 240m }));

        stale.Error!.Code.Should().Be(ErrorCodes.ConcurrencyConflict);
        stale.Error.Data.Should().BeOfType<MenuItemDto>().Which.Price.Should().Be(230m);
    }

    [Fact]
    public async Task UpdateItem_ChangingOnlyModifierGroups_StillBumpsTheRowVersion()
    {
        var setup = await SetupAsync();
        var item = await CreateItemAsync(setup, "Chicken 65");

        var result = await Call<IMenuItemService, MenuItemDto>(s => s.UpdateAsync(item.Id, UpdateRequest(item) with { ModifierGroupIds = new[] { setup.GroupId } }));

        result.Value.ModifierGroupIds.Should().Equal(setup.GroupId);
        result.Value.RowVersion.Should().NotBe(item.RowVersion);
    }

    [Fact]
    public async Task DeactivatingAStationOrTaxInUse_IsRejected()
    {
        var setup = await SetupAsync();
        await CreateItemAsync(setup, "Chicken 65");

        var station = await Call<IStationService, StationDto>(s => s.DeactivateAsync(setup.StationId));
        var tax = await Call<ITaxService, TaxDto>(s => s.DeactivateAsync(setup.TaxId));

        station.Error!.Code.Should().Be(ErrorCodes.BusinessRule);
        tax.Error!.Code.Should().Be(ErrorCodes.BusinessRule);
    }

    [Fact]
    public async Task DeactivatingACategoryWithItems_NeedsConfirmation_ThenDeactivatesTheItemsToo()
    {
        var setup = await SetupAsync();
        var item = await CreateItemAsync(setup, "Chicken 65");

        var refused = await Call<ICategoryService, CategoryDto>(s => s.DeactivateAsync(setup.CategoryId, deactivateItems: false));
        var confirmed = await Call<ICategoryService, CategoryDto>(s => s.DeactivateAsync(setup.CategoryId, deactivateItems: true));

        refused.Error!.Code.Should().Be(ErrorCodes.BusinessRule);
        confirmed.Value.IsActive.Should().BeFalse();
        (await QueryAsync(db => db.MenuItems.SingleAsync(i => i.Id == item.Id))).IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task EveryMenuWrite_BumpsTheVersion_AndPublishesMenuChangedOnce()
    {
        var before = await Call<IMenuQuery, MenuDto>(q => q.GetMenuAsync(null));
        var setup = await SetupAsync();
        var item = await CreateItemAsync(setup, "Chicken 65");
        Realtime.Reset();

        await Call<IMenuItemService, MenuItemDto>(s => s.SetAvailabilityAsync(item.Id, false));
        var after = await Call<IMenuQuery, MenuDto>(q => q.GetMenuAsync(null));

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
        var start = (await Call<IMenuQuery, MenuDto>(q => q.GetMenuAsync(null))).Version;

        await Task.WhenAll(Enumerable.Range(1, 6).Select(i =>
            Call<ICategoryService, CategoryDto>(s => s.CreateAsync(new CreateCategoryRequest { Name = $"Parallel {i}", SortOrder = i }))));

        (await Call<IMenuQuery, MenuDto>(q => q.GetMenuAsync(null))).Version.Should().Be(start + 6);
    }

    [Fact]
    public async Task GetMenu_LeavesOutInactiveData_MarksSoldOut_AndAnswersNotModifiedForTheCurrentVersion()
    {
        var setup = await SetupAsync();
        var kept = await CreateItemAsync(setup, "Chicken 65", withGroup: true);
        var removed = await CreateItemAsync(setup, "Old dish");
        await Call<IMenuItemService, MenuItemDto>(s => s.DeactivateAsync(removed.Id));
        await Call<IMenuItemService, MenuItemDto>(s => s.SetAvailabilityAsync(kept.Id, false));

        var menu = await Call<IMenuQuery, MenuDto>(q => q.GetMenuAsync(null));
        var again = await Call<IMenuQuery, MenuDto>(q => q.GetMenuAsync(menu.Version));

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
        var category = await Call<ICategoryService, CategoryDto>(s => s.CreateAsync(new CreateCategoryRequest { Name = "Starters", SortOrder = 1 }));
        var station = await Call<IStationService, StationDto>(s => s.CreateAsync(new SaveStationRequest { Name = "Grill", Code = "GRILL", SortOrder = 2 }));
        var tax = await Call<ITaxService, TaxDto>(s => s.CreateAsync(new SaveTaxRequest { Name = "GST 5%", Code = "GST5", RatePercent = 5m }));
        var group = await Call<IModifierService, ModifierGroupDto>(s => s.CreateAsync(new SaveModifierGroupRequest { Name = "Spice level", MinSelections = 1, MaxSelections = 1 }));
        await Call<IModifierService, ModifierGroupDto>(s => s.AddOptionAsync(group.Value.Id, new SaveModifierOptionRequest { Name = "Spicy", SortOrder = 2 }));
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
        var result = await Call<IMenuItemService, MenuItemDto>(s => s.CreateAsync(request));
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        return result.Value;
    }

    private async Task<Result<MenuItemDto>> UploadAsync(int itemId, byte[] content)
    {
        await using var stream = new MemoryStream(content);
        return await Call<IMenuItemService, MenuItemDto>(s => s.UploadImageAsync(itemId, stream, content.Length));
    }

    private async Task<Result<T>> Call<TService, T>(Func<TService, Task<Result<T>>> call)
        where TService : notnull
    {
        await using var scope = Fixture.Services.CreateAsyncScope();
        return await call(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private async Task<T> Call<TService, T>(Func<TService, Task<T>> call)
        where TService : notnull
    {
        await using var scope = Fixture.Services.CreateAsyncScope();
        return await call(scope.ServiceProvider.GetRequiredService<TService>());
    }
}
