using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Kitchen;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Desktop.Modules.Kitchen;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Menu;
using HotelPOS.Desktop.Services.Ui;
using HotelPOS.Desktop.Tests.Support;
using NSubstitute;

namespace HotelPOS.Desktop.Tests.Kitchen;

public sealed class KitchenDisplayViewModelTests : IDisposable
{
    private static readonly DateTime Now = DateTime.UtcNow;

    private readonly IKitchenApi _api = Substitute.For<IKitchenApi>();
    private readonly FakeRealtimeClient _realtime = new();
    private readonly ISoundPlayer _sound = Substitute.For<ISoundPlayer>();
    private readonly InMemorySettings _settings = InMemorySettings.Configured();
    private readonly KitchenDisplayViewModel _vm;

    public KitchenDisplayViewModelTests()
    {
        var menu = Substitute.For<IMenuApi>();
        menu.GetStationsAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(ApiResult<List<StationDto>>.Ok(new List<StationDto>
        {
            new() { Id = 1, Name = "Main Kitchen", Code = "MAIN", IsActive = true },
            new() { Id = 2, Name = "Bar", Code = "BAR", IsActive = true },
        }));
        var system = Substitute.For<ISystemApi>();
        system.GetPublicSettingsAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<List<SettingDto>>.Ok(new List<SettingDto>
        {
            new() { Key = SettingKeys.KitchenWarnMinutes, Value = "10" },
            new() { Key = SettingKeys.KitchenLateMinutes, Value = "20" },
        }));
        ReturnsOpen(Ticket(1, KitchenOrderStatus.New), Ticket(2, KitchenOrderStatus.Preparing, minutesAgo: 12));
        _vm = new KitchenDisplayViewModel(_api, menu, Substitute.For<IMenuCache>(), system, _realtime, _settings, _sound, Substitute.For<INotificationService>());
    }

    public void Dispose() => _vm.Dispose();

    [Fact]
    public async Task Loading_PlacesTicketsInTheirColumns_WithTimerColours()
    {
        await _vm.OnNavigatedToAsync(null);

        _vm.NewTickets.Select(c => c.Id).Should().Equal(1);
        _vm.PreparingTickets.Select(c => c.Id).Should().Equal(2);
        _vm.NewTickets[0].Urgency.Should().Be(TicketUrgency.Normal);
        _vm.PreparingTickets[0].Urgency.Should().Be(TicketUrgency.Warn);
        _vm.NewHeader.Should().Be("NEW · 1");
    }

    [Fact]
    public void TimerColour_FollowsTheWarnAndLateThresholds()
    {
        var card = new TicketCardViewModel(Ticket(9, KitchenOrderStatus.Preparing) with { CreatedAtUtc = Now });

        card.Tick(Now.AddMinutes(9), 10, 20);
        card.Urgency.Should().Be(TicketUrgency.Normal);
        card.Tick(Now.AddMinutes(10), 10, 20);
        card.Urgency.Should().Be(TicketUrgency.Warn);
        card.Tick(Now.AddMinutes(25), 10, 20);
        card.Urgency.Should().Be(TicketUrgency.Late);
        card.ElapsedText.Should().Be("25 min");
    }

    [Fact]
    public async Task AnUpdateEvent_MovesTheCard_AndADuplicateChangesNothing()
    {
        await _vm.OnNavigatedToAsync(null);
        _api.GetAsync(1, Arg.Any<CancellationToken>()).Returns(ApiResult<KitchenTicketDto>.Ok(Ticket(1, KitchenOrderStatus.Ready)));
        var change = new KitchenTicketUpdatedEvent { TicketId = 1, StationId = 1, Status = KitchenOrderStatus.Ready };

        _realtime.Publish(HubEvents.KitchenTicketUpdated, change);
        _realtime.Publish(HubEvents.KitchenTicketUpdated, change);
        await WaitUntilAsync(() => _vm.ReadyTickets.Count == 1);

        _vm.NewTickets.Should().BeEmpty();
        _vm.ReadyTickets.Should().ContainSingle().Which.Id.Should().Be(1);
        _vm.AllCards.Should().HaveCount(2);
    }

    [Fact]
    public async Task ANewTicket_AppearsOnce_WithOneSound_AndCompletedTicketsLeaveTheBoard()
    {
        await _vm.OnNavigatedToAsync(null);
        _api.GetAsync(3, Arg.Any<CancellationToken>()).Returns(ApiResult<KitchenTicketDto>.Ok(Ticket(3, KitchenOrderStatus.New)));
        _api.GetAsync(2, Arg.Any<CancellationToken>()).Returns(ApiResult<KitchenTicketDto>.Ok(Ticket(2, KitchenOrderStatus.Completed)));

        _realtime.Publish(HubEvents.KitchenTicketCreated, new KitchenTicketCreatedEvent { TicketId = 3, StationId = 1 });
        _realtime.Publish(HubEvents.KitchenTicketCreated, new KitchenTicketCreatedEvent { TicketId = 3, StationId = 1 });
        _realtime.Publish(HubEvents.KitchenTicketUpdated, new KitchenTicketUpdatedEvent { TicketId = 2, StationId = 1 });
        await WaitUntilAsync(() => _vm.NewTickets.Count == 2 && _vm.PreparingTickets.Count == 0);

        _vm.NewTickets.Select(c => c.Id).Should().Equal(1, 3);
        _vm.NewTickets[1].IsHighlighted.Should().BeTrue();
        _sound.Received(1).NewTicket();
    }

    [Fact]
    public async Task ChoosingAStation_IsRemembered_JoinsItOnTheHub_AndIgnoresOtherStations()
    {
        await _vm.OnNavigatedToAsync(null);

        _vm.SelectedStation = _vm.Stations.Single(s => s.Name == "Bar");
        _realtime.Publish(HubEvents.KitchenTicketCreated, new KitchenTicketCreatedEvent { TicketId = 7, StationId = 1 });

        _settings.Current.StationId.Should().Be(2);
        _realtime.StationId.Should().Be(2);
        await _api.Received().GetOpenAsync(2, Arg.Any<CancellationToken>());
        await _api.DidNotReceive().GetAsync(7, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refresh_ReplacesTheBoardWithTheServersState()
    {
        await _vm.OnNavigatedToAsync(null);
        ReturnsOpen(Ticket(5, KitchenOrderStatus.Ready));

        await _vm.RefreshAsync();

        _vm.AllCards.Select(c => c.Id).Should().Equal(5);
        _vm.ReadyTickets.Should().ContainSingle();
    }

    private void ReturnsOpen(params KitchenTicketDto[] tickets) =>
        _api.GetOpenAsync(Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult<KitchenTicketListDto>.Ok(new KitchenTicketListDto { Tickets = tickets, ServerTimeUtc = DateTime.UtcNow }));

    private static KitchenTicketDto Ticket(int id, KitchenOrderStatus status, int minutesAgo = 1) => new()
    {
        Id = id,
        TicketNumber = $"10{id:00}-1",
        TableCode = "T0" + id,
        StationId = 1,
        Status = status,
        CreatedAtUtc = Now.AddMinutes(-minutesAgo).AddSeconds(id),
        Items = new[] { new KitchenTicketItemDto { OrderItemId = id, Name = "Chicken Biryani", Quantity = 2 } },
    };

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 50 && !condition(); i++)
        {
            await Task.Delay(20);
        }

        condition().Should().BeTrue();
    }
}
