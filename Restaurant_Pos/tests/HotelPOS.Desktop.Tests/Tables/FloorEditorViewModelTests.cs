using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Desktop.Modules.Admin;
using HotelPOS.Desktop.Modules.Tables;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Auth;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Ui;
using HotelPOS.Desktop.Tests.Support;
using NSubstitute;

namespace HotelPOS.Desktop.Tests.Tables;

/// <summary>Phase 22 P2: the floor-plan editor (snap, clamp, undo/redo, save, tray, reset) and the map's List/Map rule.</summary>
public sealed class FloorEditorViewModelTests : IDisposable
{
    private readonly IFloorLayoutApi _layoutApi = Substitute.For<IFloorLayoutApi>();
    private readonly IFloorApi _floorApi = Substitute.For<IFloorApi>();
    private readonly FakeRealtimeClient _realtime = new();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly FloorEditorViewModel _vm;

    public FloorEditorViewModelTests()
    {
        _floorApi.GetSectionsAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult<List<SectionDto>>.Ok(new List<SectionDto> { new() { Id = 1, Name = "Ground Floor", SortOrder = 1, IsActive = true } }));
        _layoutApi.GetAsync(1, Arg.Any<CancellationToken>()).Returns(ApiResult<FloorLayoutDto>.Ok(Layout()));
        _vm = new FloorEditorViewModel(_layoutApi, _floorApi, _realtime, _notifications);
    }

    public void Dispose() => _vm.Dispose();

    private static FloorLayoutDto Layout() => new()
    {
        SectionId = 1,
        SectionName = "Ground Floor",
        Tables = new[]
        {
            new FloorTableDto { TableId = 1, Code = "T01", Capacity = 4, PosX = 24, PosY = 24, Width = 120, Height = 80, Shape = TableShape.Rectangle, Rotation = 0, HasLayout = true },
            new FloorTableDto { TableId = 2, Code = "T02", Capacity = 2, PosX = 168, PosY = 24, Width = 120, Height = 80, Shape = TableShape.Round, Rotation = 90, HasLayout = true },
            new FloorTableDto { TableId = 3, Code = "T03", Capacity = 6 },
        },
    };

    private async Task<FloorEditorViewModel> LoadedAsync()
    {
        await _vm.OnNavigatedToAsync(null);
        return _vm;
    }

    [Fact]
    public async Task Load_SplitsPlacedFromTheUnplacedTray()
    {
        var vm = await LoadedAsync();

        vm.Placed.Select(i => i.Code).Should().Equal("T01", "T02");
        vm.Unplaced.Select(i => i.Code).Should().Equal("T03");
        vm.IsDirty.Should().BeFalse();
        vm.SaveCommand.CanExecute(null).Should().BeFalse("nothing to save yet");
    }

    [Theory]
    [InlineData(96, 96)]
    [InlineData(100, 104)]
    [InlineData(103, 104)]
    [InlineData(99, 96)]
    [InlineData(4, 8)]
    public async Task Move_SnapsToTheEightUnitGrid(int dragged, int expected)
    {
        var vm = await LoadedAsync();

        vm.MoveTo(vm.Placed[0], dragged, dragged);

        vm.Placed[0].X.Should().Be(expected);
        vm.Placed[0].Y.Should().Be(expected);
    }

    [Fact]
    public async Task Move_WithSnapOff_KeepsTheExactPosition()
    {
        var vm = await LoadedAsync();
        vm.SnapEnabled = false;

        vm.MoveTo(vm.Placed[0], 103, 99);

        (vm.Placed[0].X, vm.Placed[0].Y).Should().Be((103, 99));
    }

    [Fact]
    public async Task MoveAndResize_AreClampedToTheBackendLimits()
    {
        var vm = await LoadedAsync();
        var table = vm.Placed[0];

        vm.MoveTo(table, -50, 9000);
        (table.X, table.Y).Should().Be((FloorLayoutLimits.MinCoordinate, FloorLayoutLimits.MaxCoordinate));

        vm.ResizeTo(table, 0, 0, 10, 5000);
        (table.Width, table.Height).Should().Be((FloorLayoutLimits.MinSize, FloorLayoutLimits.MaxSize));
    }

    [Fact]
    public async Task Undo_RestoresAMove_AndRedoReappliesIt()
    {
        var vm = await LoadedAsync();
        var table = vm.Placed[0];

        vm.MoveTo(table, 400, 240);
        vm.IsDirty.Should().BeTrue();
        vm.UndoCommand.Execute(null);

        (table.X, table.Y).Should().Be((24, 24));
        vm.IsDirty.Should().BeFalse("back at the saved layout");

        vm.RedoCommand.Execute(null);
        (table.X, table.Y).Should().Be((400, 240));
        vm.IsDirty.Should().BeTrue();
    }

    [Fact]
    public async Task Gesture_RecordsOneUndoStep_ForAWholeDrag()
    {
        var vm = await LoadedAsync();
        var table = vm.Placed[0];

        vm.BeginGesture();
        vm.MoveTo(table, 80, 80);
        vm.MoveTo(table, 160, 160);
        vm.MoveTo(table, 320, 200);
        vm.EndGesture();
        vm.UndoCommand.Execute(null);

        (table.X, table.Y).Should().Be((24, 24));
        vm.UndoCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Rotate_AndShapeToggle_ChangeTheSelectedTable()
    {
        var vm = await LoadedAsync();
        vm.Select(vm.Placed[0]);

        vm.RotateCommand.Execute(null);
        vm.RotateCommand.Execute(null);
        vm.ToggleShapeCommand.Execute(null);

        vm.Placed[0].Rotation.Should().Be(180);
        vm.Placed[0].Shape.Should().Be(TableShape.Round);
    }

    [Fact]
    public async Task Nudge_MovesTheSelectionByOneGridStep()
    {
        var vm = await LoadedAsync();
        vm.Select(vm.Placed[0]);

        vm.Nudge(FloorEditorViewModel.GridSize, 0);

        vm.Placed[0].X.Should().Be(32);
    }

    [Fact]
    public async Task Place_PutsATrayTableOnTheNextFreeGridSlot()
    {
        var vm = await LoadedAsync();

        vm.PlaceCommand.Execute(vm.Unplaced[0]);

        vm.Unplaced.Should().BeEmpty();
        var placed = vm.Placed.Single(i => i.Code == "T03");
        // Slots 0 (24,24) and 1 (168,24) are taken by T01 and T02, so the third slot is used.
        (placed.X, placed.Y, placed.Width, placed.Height).Should().Be((312, 24, 120, 80));
    }

    [Fact]
    public async Task ResetToGrid_RelaysEveryTableSixPerRow()
    {
        var vm = await LoadedAsync();
        vm.MoveTo(vm.Placed[0], 800, 600);

        vm.ResetToGridCommand.Execute(null);

        vm.Unplaced.Should().BeEmpty("reset places every table");
        vm.Placed.Select(i => (i.X, i.Y, i.Width, i.Height, i.Rotation)).Should().Equal(
            (24, 24, 120, 80, 0), (168, 24, 120, 80, 0), (312, 24, 120, 80, 0));
        vm.Placed.Should().OnlyContain(i => i.Shape == TableShape.Rectangle);
    }

    [Fact]
    public async Task Save_SendsExactlyThePlacedItems_AndClearsTheDirtyMark()
    {
        var vm = await LoadedAsync();
        vm.MoveTo(vm.Placed[1], 440, 160);
        SaveFloorLayoutRequest? sent = null;
        _layoutApi.SaveAsync(1, Arg.Do<SaveFloorLayoutRequest>(r => sent = r), Arg.Any<CancellationToken>())
            .Returns(ApiResult<FloorLayoutDto>.Ok(Layout()));

        await vm.SaveCommand.ExecuteAsync(null);

        sent.Should().NotBeNull();
        sent!.Items.Select(i => i.TableId).Should().Equal(1, 2);
        var second = sent.Items.Single(i => i.TableId == 2);
        (second.PosX, second.PosY, second.Width, second.Height, second.Shape, second.Rotation)
            .Should().Be((440, 160, 120, 80, TableShape.Round, 90));
        vm.IsDirty.Should().BeFalse();
        _notifications.Received(1).Success(Arg.Any<string>());
    }

    [Fact]
    public async Task Save_Failure_ShowsTheError_AndReloadsTheStoredLayout()
    {
        var vm = await LoadedAsync();
        vm.MoveTo(vm.Placed[0], 400, 400);
        _layoutApi.SaveAsync(1, Arg.Any<SaveFloorLayoutRequest>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult<FloorLayoutDto>.Fail("conflict", "The layout changed on another terminal."));

        await vm.SaveCommand.ExecuteAsync(null);

        _notifications.Received(1).Error(Arg.Any<string>(), Arg.Any<string?>());
        await _layoutApi.Received(2).GetAsync(1, Arg.Any<CancellationToken>());
        vm.IsDirty.Should().BeFalse("the stored layout was fetched again");
        vm.Placed[0].X.Should().Be(24);
    }

    [Fact]
    public async Task LayoutChangedEvent_ReloadsWhenClean_AndWarnsWhenDirty()
    {
        var vm = await LoadedAsync();

        _realtime.Publish(HubEvents.FloorLayoutChanged, new FloorLayoutChangedEvent { SectionId = 1 });
        await _layoutApi.Received(2).GetAsync(1, Arg.Any<CancellationToken>());

        vm.MoveTo(vm.Placed[0], 400, 400);
        _realtime.Publish(HubEvents.FloorLayoutChanged, new FloorLayoutChangedEvent { SectionId = 1 });
        _notifications.Received(1).Warning(Arg.Any<string>());
        vm.Placed[0].X.Should().Be(400, "unsaved edits are not thrown away");
    }

    // ---- the table map: List / Map ----

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void Map_UsesTheCanvasOnlyInMapModeWithPlacedTables(bool mapMode, bool hasPlaced, bool expected) =>
        TableMapSectionViewModel.ShouldUseCanvas(mapMode, hasPlaced).Should().Be(expected);

    [Fact]
    public async Task TableMap_FallsBackToTheListForASectionWithoutPlacedTables_AndRemembersTheMode()
    {
        var preferences = Substitute.For<HotelPOS.Desktop.Services.Configuration.IUserPreferences>();
        var api = Substitute.For<IFloorApi>();
        TableDto T(int id, int section, bool placed) => new()
        {
            Id = id, Code = $"T{id}", SectionId = section, SectionName = $"S{section}", Capacity = 4, IsActive = true, RowVersion = "v",
            PosX = placed ? 24 : null, PosY = placed ? 24 : null, Width = placed ? 120 : null, Height = placed ? 80 : null,
            Shape = placed ? TableShape.Rectangle : null, Rotation = placed ? 0 : null,
        };

        api.GetMapAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(ApiResult<TableMapDto>.Ok(new TableMapDto
        {
            ServerTimeUtc = DateTime.UtcNow,
            Sections = new[]
            {
                new TableMapSectionDto { Id = 1, Name = "S1", IsActive = true, Tables = new[] { T(1, 1, true) } },
                new TableMapSectionDto { Id = 2, Name = "S2", IsActive = true, Tables = new[] { T(2, 2, false) } },
            },
        }));
        var session = new AuthSession(new InMemorySecureStore());
        session.Start(TestData.Login("waiter1", roles: "Waiter"));
        using var map = new TableMapViewModel(api, Substitute.For<IOrdersApi>(), Substitute.For<HotelPOS.Desktop.Services.Orders.ILocalDraftStore>(),
            _realtime, Substitute.For<INavigationService>(), session, Substitute.For<IDialogService>(), Substitute.For<INotificationService>(),
            preferences: preferences);

        await map.OnNavigatedToAsync(null);

        map.IsMapMode.Should().BeTrue();
        map.Sections.Single(s => s.Id == 1).UseCanvas.Should().BeTrue();
        map.Sections.Single(s => s.Id == 2).UseCanvas.Should().BeFalse("no placed tables: today's tile list");

        map.ViewMode = TableMapViewModel.ListMode;

        map.Sections.Single(s => s.Id == 1).UseCanvas.Should().BeFalse();
        preferences.TableMapMode.Should().Be("List");

        _realtime.Publish(HubEvents.FloorLayoutChanged, new FloorLayoutChangedEvent { SectionId = 1 });
        await api.Received(2).GetMapAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }
}
