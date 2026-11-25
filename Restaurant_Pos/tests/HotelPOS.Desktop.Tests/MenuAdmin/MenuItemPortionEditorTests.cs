using HotelPOS.Contracts.Menu;
using HotelPOS.Desktop.Modules.MenuAdmin;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Menu;
using HotelPOS.Desktop.Services.Ui;
using NSubstitute;

namespace HotelPOS.Desktop.Tests.MenuAdmin;

public class MenuItemPortionEditorTests
{
    private static readonly CategoryDto Mains = new() { Id = 1, Name = "Mains", IsActive = true };
    private static readonly StationDto Grill = new() { Id = 3, Name = "Grill", IsActive = true };

    private static MenuItemEditorViewModel NewEditor()
    {
        var editor = new MenuItemEditorViewModel();
        editor.SetLookups(new[] { Mains }, new[] { new TaxDto { Id = 2, Name = "GST 5%", IsActive = true } }, new[] { Grill }, Array.Empty<ModifierGroupDto>());
        editor.BeginCreate(null);
        editor.Name = "Chicken Biryani";
        return editor;
    }

    private static void Fill(MenuItemEditorViewModel editor, params (string Name, string Price, bool Default, bool On, string Qty, string Unit)[] rows)
    {
        editor.Portions.Clear();
        foreach (var r in rows)
        {
            editor.Portions.Add(new PortionRowViewModel { Name = r.Name, PriceText = r.Price, IsDefault = r.Default, IsActive = r.On, QuantityText = r.Qty, Unit = r.Unit });
        }
    }

    [Fact]
    public void NewItem_StartsWithOneRegularRow_ThatIsTheDefault()
    {
        var editor = NewEditor();

        var row = editor.Portions.Should().ContainSingle().Subject;
        (row.Name, row.IsDefault, row.IsActive, row.PriceText).Should().Be(("Regular", true, true, string.Empty));
    }

    [Fact]
    public void AddRemoveRow_AndTheDefaultRadioKeepsExactlyOneDefault()
    {
        var editor = NewEditor();
        editor.Portions[0].PriceText = "100";

        editor.AddPortionCommand.Execute(null);
        editor.AddPortionCommand.Execute(null);
        editor.Portions[1].Name = "Half";
        editor.Portions[2].Name = "Full";
        editor.SetDefaultCommand.Execute(editor.Portions[2]);

        editor.Portions.Count(p => p.IsDefault).Should().Be(1);
        editor.Portions[2].IsDefault.Should().BeTrue();
        editor.Portions[2].IsActive.Should().BeTrue("a default portion is always on");

        editor.RemovePortionCommand.Execute(editor.Portions[2]);

        editor.Portions.Should().HaveCount(2);
        editor.Portions.Count(p => p.IsDefault).Should().Be(1, "removing the default hands it to the first row left");
        editor.Portions[0].IsDefault.Should().BeTrue();

        editor.RemovePortionCommand.Execute(editor.Portions[1]);
        editor.RemovePortionCommand.Execute(editor.Portions[0]);
        editor.Portions.Should().HaveCount(1, "the last portion cannot be removed");
    }

    [Fact]
    public void AddPortion_StopsAtTwelveRows()
    {
        var editor = NewEditor();

        for (var i = 0; i < 20; i++)
        {
            editor.AddPortionCommand.Execute(null);
        }

        editor.Portions.Should().HaveCount(MenuLimits.MaxPortions);
    }

    [Theory]
    [InlineData("duplicate", "Half,100,true,true,,;half,120,false,true,,", "once")]
    [InlineData("blank name", "Half,100,true,true,,; ,120,false,true,,", "name")]
    [InlineData("bad price", "Half,abc,true,true,,", "price")]
    [InlineData("negative", "Half,-5,true,true,,", "price")]
    [InlineData("no price", "Half,,true,true,,", "price")]
    [InlineData("none on", "Half,100,true,false,,", "on")]
    [InlineData("default off", "Half,100,true,false,,;Full,200,false,true,,", "default")]
    [InlineData("no default", "Half,100,false,true,,;Full,200,false,true,,", "default")]
    [InlineData("zero quantity", "Half,100,true,true,0,GM", "quantity")]
    [InlineData("unit alone", "Half,100,true,true,,GM", "quantity")]
    public void Validate_BlocksSave_WithARowLevelMessage(string why, string rows, string word)
    {
        var editor = NewEditor();
        Fill(editor, rows.Split(';').Select(r => r.Split(',')).Select(p =>
            (p[0], p[1], bool.Parse(p[2]), bool.Parse(p[3]), p[4], p[5])).ToArray());

        var message = editor.Validate();

        message.Should().NotBeNull(why);
        message!.ToLowerInvariant().Should().Contain(word, why);
        editor.Portions.Any(p => p.Error is not null).Should().BeTrue($"{why}: the offending row carries the message");
    }

    [Fact]
    public void Validate_AcceptsAGoodTable_AndClearsRowErrors()
    {
        var editor = NewEditor();
        Fill(editor,
            ("Quarter", "100", false, true, "250", "GM"),
            ("Half", "180", true, true, "500", "GM"),
            ("Full", "320.50", false, true, string.Empty, string.Empty),
            ("1 KG", "550", false, false, "1", "KG"));

        editor.Validate().Should().BeNull();

        editor.Portions.Should().OnlyContain(p => p.Error == null);
    }

    [Fact]
    public void ToCreateRequest_CarriesThePortionsInOrder_AndPriceFollowsTheDefault()
    {
        var editor = NewEditor();
        Fill(editor,
            ("Quarter", "100", false, true, "250", "GM"),
            ("Half", "180", true, true, "500", "GM"),
            ("1 KG", "550", false, false, "1", "KG"));

        var request = editor.ToCreateRequest();

        request.Price.Should().Be(180m);
        request.Portions!.Select(p => (p.Name, p.Price, p.IsDefault, p.IsActive, p.SortOrder, p.Quantity, p.Unit)).Should().Equal(
            ("Quarter", 100m, false, true, 1, (decimal?)250m, "GM"),
            ("Half", 180m, true, true, 2, (decimal?)500m, "GM"),
            ("1 KG", 550m, false, false, 3, (decimal?)1m, "KG"));
    }

    [Fact]
    public void BeginEdit_LoadsThePortionsInSortOrder_WithTheirIds()
    {
        var editor = NewEditor();
        var dto = new MenuItemDto
        {
            Id = 9, CategoryId = 1, Name = "Chicken Biryani", PreparationStationId = 3, Price = 180m, RowVersion = "v1",
            Portions = new[]
            {
                new MenuItemPortionDto { Id = 21, Name = "Half", Quantity = 500, Unit = "GM", Price = 180m, IsDefault = true, IsActive = true, SortOrder = 2 },
                new MenuItemPortionDto { Id = 20, Name = "Quarter", Price = 100m, IsActive = true, SortOrder = 1 },
            },
        };

        editor.BeginEdit(dto);

        editor.Portions.Select(p => (p.Id, p.Name, p.PriceText, p.IsDefault, p.QuantityText, p.Unit)).Should().Equal(
            ((int?)20, "Quarter", "100", false, string.Empty, string.Empty), ((int?)21, "Half", "180", true, "500", "GM"));
        editor.ToUpdateRequest().Portions!.Select(p => p.Id).Should().Equal(20, 21);
    }

    [Fact]
    public void ListRow_ShowsPortionChipsWithPricesAndTheDefaultMarked()
    {
        var dto = new MenuItemDto
        {
            Id = 9, Name = "Chicken Biryani", CategoryName = "Mains", Price = 180m,
            Portions = new[]
            {
                new MenuItemPortionDto { Id = 1, Name = "Quarter", Price = 100m, IsActive = true, SortOrder = 1 },
                new MenuItemPortionDto { Id = 2, Name = "Half", Price = 180m, IsDefault = true, IsActive = true, SortOrder = 2 },
                new MenuItemPortionDto { Id = 3, Name = "1 KG", Price = 550m, IsActive = false, SortOrder = 3 },
            },
        };

        var row = new MenuItemRowViewModel(dto);

        row.PortionChips.Select(c => (c.Text, c.IsDefault, c.IsOff)).Should().Equal(
            ("Quarter 100", false, false), ("Half 180", true, false), ("1 KG 550", false, true));
        row.Initials.Should().Be("CB");
    }

    [Fact]
    public async Task PhotoChosenBeforeSave_IsUploadedAfterTheItemIsCreated()
    {
        var api = Substitute.For<IMenuApi>();
        api.GetCategoriesAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(ApiResult<List<CategoryDto>>.Ok(new List<CategoryDto> { Mains }));
        api.GetTaxesAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(ApiResult<List<TaxDto>>.Ok(new List<TaxDto>()));
        api.GetStationsAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(ApiResult<List<StationDto>>.Ok(new List<StationDto> { Grill }));
        api.GetModifierGroupsAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(ApiResult<List<ModifierGroupDto>>.Ok(new List<ModifierGroupDto>()));
        api.GetItemsAsync(Arg.Any<MenuItemQuery>(), Arg.Any<CancellationToken>()).Returns(ApiResult<List<MenuItemDto>>.Ok(new List<MenuItemDto>()));
        var created = new MenuItemDto { Id = 7, CategoryId = 1, Name = "Chicken Biryani", PreparationStationId = 3, Price = 180m, RowVersion = "v1" };
        api.CreateItemAsync(Arg.Any<CreateMenuItemRequest>(), Arg.Any<CancellationToken>()).Returns(ApiResult<MenuItemDto>.Ok(created));
        api.UploadImageAsync(7, Arg.Any<Stream>(), "biryani.png", Arg.Any<CancellationToken>())
            .Returns(ApiResult<MenuItemDto>.Ok(created with { ImageUrl = "/images/menu/7-abc.jpg", RowVersion = "v2" }));
        var path = Path.Combine(Path.GetTempPath(), "biryani.png");
        await File.WriteAllBytesAsync(path, new byte[] { 1, 2, 3, 4 });
        var picker = Substitute.For<IFilePicker>();
        picker.PickImage().Returns(path);
        var vm = new MenuItemsPageViewModel(api, Substitute.For<IMenuCache>(), Substitute.For<IDialogService>(), Substitute.For<INotificationService>(), picker);
        await vm.LoadAsync();

        vm.NewItemCommand.Execute(null);
        vm.Editor.Name = "Chicken Biryani";
        vm.Editor.Portions[0].PriceText = "180";
        await vm.ChoosePictureCommand.ExecuteAsync(null);
        vm.Editor.HasPicture.Should().BeTrue("the chosen photo shows before the item is saved");
        await api.DidNotReceive().UploadImageAsync(Arg.Any<int>(), Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        await vm.SaveItemCommand.ExecuteAsync(null);

        await api.Received(1).UploadImageAsync(7, Arg.Any<Stream>(), "biryani.png", Arg.Any<CancellationToken>());
        vm.Editor.IsNew.Should().BeFalse();
        vm.Editor.ImageUrl.Should().Be("/images/menu/7-abc.jpg");
        File.Delete(path);
    }

    [Fact]
    public async Task APendingPhoto_CanBeRemovedBeforeSaving_AndIsThenNotUploaded()
    {
        var api = Substitute.For<IMenuApi>();
        var picker = Substitute.For<IFilePicker>();
        var path = Path.Combine(Path.GetTempPath(), "pending.png");
        await File.WriteAllBytesAsync(path, new byte[] { 1 });
        picker.PickImage().Returns(path);
        var dialogs = Substitute.For<IDialogService>();
        dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>()).Returns(true);
        var vm = new MenuItemsPageViewModel(api, Substitute.For<IMenuCache>(), dialogs, Substitute.For<INotificationService>(), picker);
        vm.Editor.SetLookups(new[] { Mains }, Array.Empty<TaxDto>(), new[] { Grill }, Array.Empty<ModifierGroupDto>());
        vm.Editor.BeginCreate(null);

        await vm.ChoosePictureCommand.ExecuteAsync(null);
        vm.Editor.HasPicture.Should().BeTrue();
        await vm.RemovePictureCommand.ExecuteAsync(null);

        vm.Editor.HasPicture.Should().BeFalse();
        vm.Editor.PendingImagePath.Should().BeNull();
        File.Delete(path);
    }
}
