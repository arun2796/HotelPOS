using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Desktop.Modules.Tables;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Ui;
using HotelPOS.Desktop.Tests.Support;
using NSubstitute;

namespace HotelPOS.Desktop.Tests.Tables;

public sealed class TableMapViewModelTests : IDisposable
{
    private static readonly DateTime MapTime = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    private readonly IFloorApi _api = Substitute.For<IFloorApi>();
    private readonly FakeRealtimeClient _realtime = new();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly TableMapViewModel _vm;

    public TableMapViewModelTests()
    {
        _api.GetMapAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult<TableMapDto>.Ok(Map(MapTime, Table(1, "T01"), Table(2, "T02"), Table(11, "O01", sectionId: 2))));
        _dialogs.ConfirmAsync(default!, default!, default!, default!, default).ReturnsForAnyArgs(true);
        _vm = new TableMapViewModel(_api, _realtime, _dialogs, Substitute.For<INotificationService>());
    }

    public void Dispose() => _vm.Dispose();

    [Fact]
    public async Task NavigatingTo_LoadsTheMap_AndSubscribesToTableEvents()
    {
        await _vm.OnNavigatedToAsync(null);

        _vm.Sections.Select(s => s.Name).Should().Equal("Ground Floor", "Outdoor");
        _vm.Sections[0].Tables.Select(t => t.Code).Should().Equal("T01", "T02");
        _vm.Filters.Select(f => f.Name).Should().Equal("All", "Ground Floor", "Outdoor");
        _vm.SummaryText.Should().Be("3 available · 0 in use · 3 tables");
        _realtime.SubscriberCount(HubEvents.TableStatusChanged).Should().Be(1);
    }

    [Fact]
    public async Task TableStatusChanged_UpdatesTheTile_AndApplyingItTwiceChangesNothing()
    {
        await _vm.OnNavigatedToAsync(null);
        var change = Occupied(2, "T02", guests: 4, at: MapTime.AddSeconds(5));

        _realtime.Publish(HubEvents.TableStatusChanged, change);
        var tile = Tile("T02");
        var afterFirst = (tile.Status, tile.GuestCount, tile.RowVersion, _vm.SummaryText);
        _realtime.Publish(HubEvents.TableStatusChanged, change);

        tile.Status.Should().Be(TableStatus.Occupied);
        tile.GuestCount.Should().Be(4);
        tile.RowVersion.Should().Be("v2");
        (tile.Status, tile.GuestCount, tile.RowVersion, _vm.SummaryText).Should().Be(afterFirst);
        _vm.SummaryText.Should().Be("2 available · 1 in use · 3 tables");
    }

    [Fact]
    public async Task AnEventOlderThanTheMap_IsIgnored()
    {
        await _vm.OnNavigatedToAsync(null);

        _realtime.Publish(HubEvents.TableStatusChanged, Occupied(1, "T01", guests: 2, at: MapTime.AddSeconds(-1)));

        Tile("T01").Status.Should().Be(TableStatus.Available);
    }

    [Fact]
    public async Task OutOfOrderEvents_KeepTheNewestState()
    {
        await _vm.OnNavigatedToAsync(null);
        var released = new TableStatusChangedEvent
        {
            TableId = 1, TableCode = "T01", Status = TableStatus.Available, OccurredAtUtc = MapTime.AddSeconds(20), EntityVersion = "v3",
        };

        _realtime.Publish(HubEvents.TableStatusChanged, released);
        _realtime.Publish(HubEvents.TableStatusChanged, Occupied(1, "T01", guests: 2, at: MapTime.AddSeconds(10)));

        Tile("T01").Status.Should().Be(TableStatus.Available);
        Tile("T01").RowVersion.Should().Be("v3");
    }

    [Fact]
    public async Task RefreshAsync_ReplacesTheStateWithTheServers()
    {
        await _vm.OnNavigatedToAsync(null);
        _realtime.Publish(HubEvents.TableStatusChanged, Occupied(2, "T02", guests: 4, at: MapTime.AddSeconds(5)));
        _api.GetMapAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult<TableMapDto>.Ok(Map(MapTime.AddMinutes(1), Table(2, "T02"), Table(3, "T03"))));

        await _vm.RefreshAsync();

        _vm.AllTables.Select(t => t.Code).Should().BeEquivalentTo("T02", "T03");
        Tile("T02").Status.Should().Be(TableStatus.Available);
        Tile("T02").GuestCount.Should().BeNull();
        _vm.Sections.Should().ContainSingle();
    }

    [Fact]
    public async Task RefreshAsync_KeepsAnEventNewerThanTheServerSnapshot()
    {
        await _vm.OnNavigatedToAsync(null);
        // The event arrives while the refresh request is in flight; the response was read before it.
        _realtime.Publish(HubEvents.TableStatusChanged, Occupied(1, "T01", guests: 2, at: MapTime.AddSeconds(30)));
        _api.GetMapAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult<TableMapDto>.Ok(Map(MapTime.AddSeconds(20), Table(1, "T01"))));

        await _vm.RefreshAsync();

        Tile("T01").Status.Should().Be(TableStatus.Occupied);
    }

    [Fact]
    public async Task AnEventForAnUnknownTable_ReloadsTheMap()
    {
        await _vm.OnNavigatedToAsync(null);
        _api.ClearReceivedCalls();

        _realtime.Publish(HubEvents.TableStatusChanged, Occupied(99, "N01", guests: 2, at: MapTime.AddSeconds(5)));

        await _api.Received(1).GetMapAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LeavingThePage_StopsListening()
    {
        await _vm.OnNavigatedToAsync(null);

        _vm.OnNavigatedFrom();

        _realtime.SubscriberCount(HubEvents.TableStatusChanged).Should().Be(0);
    }

    [Fact]
    public async Task Occupy_SendsGuestsAndRowVersion_AndAppliesTheResult()
    {
        await _vm.OnNavigatedToAsync(null);
        _api.OccupyAsync(1, Arg.Any<OccupyTableRequest>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult<TableDto>.Ok(Table(1, "T01") with { Status = TableStatus.Occupied, GuestCount = 4, RowVersion = "v2" }));
        _vm.SelectTableCommand.Execute(Tile("T01"));

        _vm.Details.DigitCommand.Execute("4");
        await _vm.Details.OccupyCommand.ExecuteAsync(null);

        await _api.Received(1).OccupyAsync(1, Arg.Is<OccupyTableRequest>(r => r.GuestCount == 4 && r.RowVersion == "v1"), Arg.Any<CancellationToken>());
        Tile("T01").Status.Should().Be(TableStatus.Occupied);
        _vm.Details.GuestInput.Should().BeEmpty();
        _vm.Details.ErrorMessage.Should().BeNull();
        _vm.SummaryText.Should().Be("2 available · 1 in use · 3 tables");
    }

    [Fact]
    public async Task Occupy_ChangedByAnotherTerminal_ShowsTheMessage_AndTheLatestState()
    {
        await _vm.OnNavigatedToAsync(null);
        var conflict = new ApiResult<TableDto>
        {
            Success = false,
            Message = "This table was changed by another user or terminal.",
            Errors = new[] { new ApiError(ErrorCodes.ConcurrencyConflict, "conflict") },
            Data = Table(1, "T01") with { Status = TableStatus.Occupied, GuestCount = 6, RowVersion = "v9" },
        };
        _api.OccupyAsync(1, Arg.Any<OccupyTableRequest>(), Arg.Any<CancellationToken>()).Returns(conflict);
        _vm.SelectTableCommand.Execute(Tile("T01"));

        _vm.Details.DigitCommand.Execute("2");
        await _vm.Details.OccupyCommand.ExecuteAsync(null);

        _vm.Details.ErrorMessage.Should().Be(TableDetailsViewModel.ChangedByAnotherTerminal);
        Tile("T01").GuestCount.Should().Be(6);
        Tile("T01").RowVersion.Should().Be("v9");
        _vm.Details.OccupyCommand.CanExecute(null).Should().BeFalse();
        _vm.Details.ReleaseCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task Occupy_WithoutGuests_AsksForThem_AndDoesNotCallTheServer()
    {
        await _vm.OnNavigatedToAsync(null);
        _vm.SelectTableCommand.Execute(Tile("T01"));

        await _vm.Details.OccupyCommand.ExecuteAsync(null);

        _vm.Details.ErrorMessage.Should().NotBeNull();
        await _api.DidNotReceiveWithAnyArgs().OccupyAsync(default, default!, default);
    }

    [Fact]
    public void NumPad_IgnoresLeadingZero_AndLimitsToTwoDigits()
    {
        var details = _vm.Details;

        foreach (var key in new[] { "0", "1", "2", "3" })
        {
            details.DigitCommand.Execute(key);
        }

        details.GuestInput.Should().Be("12");
        details.BackspaceCommand.Execute(null);
        details.GuestInput.Should().Be("1");
    }

    [Fact]
    public async Task SectionFilter_ShowsOnlyThatSection()
    {
        await _vm.OnNavigatedToAsync(null);

        _vm.SelectedFilter = _vm.Filters.Single(f => f.Name == "Outdoor");

        _vm.Sections.Single(s => s.Name == "Ground Floor").IsVisible.Should().BeFalse();
        _vm.Sections.Single(s => s.Name == "Outdoor").IsVisible.Should().BeTrue();
    }

    private TableTileViewModel Tile(string code) => _vm.AllTables.Single(t => t.Code == code);

    private static TableStatusChangedEvent Occupied(int id, string code, int guests, DateTime at) => new()
    {
        TableId = id,
        EntityId = id,
        TableCode = code,
        Status = TableStatus.Occupied,
        GuestCount = guests,
        OccupiedAtUtc = at,
        OccurredAtUtc = at,
        EntityVersion = "v2",
    };

    private static TableDto Table(int id, string code, int sectionId = 1) => new()
    {
        Id = id,
        Code = code,
        SectionId = sectionId,
        SectionName = sectionId == 1 ? "Ground Floor" : "Outdoor",
        Capacity = 4,
        Status = TableStatus.Available,
        IsActive = true,
        RowVersion = "v1",
    };

    private static TableMapDto Map(DateTime serverTime, params TableDto[] tables) => new()
    {
        ServerTimeUtc = serverTime,
        Sections = tables.GroupBy(t => (t.SectionId, t.SectionName))
            .OrderBy(g => g.Key.SectionId)
            .Select(g => new TableMapSectionDto { Id = g.Key.SectionId, Name = g.Key.SectionName, IsActive = true, Tables = g.ToList() })
            .ToList(),
    };
}
