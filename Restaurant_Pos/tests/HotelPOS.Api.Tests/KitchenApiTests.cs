using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using HotelPOS.Api.Tests.Support;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Kitchen;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Orders;
using HotelPOS.Contracts.Realtime;
using Microsoft.AspNetCore.SignalR.Client;

namespace HotelPOS.Api.Tests;

public class KitchenApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public KitchenApiTests(ApiFactory factory)
    {
        _factory = factory;
    }

    public static TheoryData<string, string, string, HttpStatusCode> Matrix => new()
    {
        { "kitchen1", "GET", "/api/kitchen/orders", HttpStatusCode.OK },
        { "manager1", "GET", "/api/kitchen/orders/completed", HttpStatusCode.OK },
        { "waiter1", "GET", "/api/kitchen/orders", HttpStatusCode.Forbidden },
        { "cashier1", "POST", "/api/kitchen/orders/1/ready", HttpStatusCode.Forbidden },
        { "waiter1", "POST", "/api/kitchen/orders/1/start", HttpStatusCode.Forbidden },
        { "kitchen1", "POST", "/api/orders/1/serve", HttpStatusCode.Forbidden },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Endpoint_ReturnsExpectedStatus_ForRole(string username, string method, string url, HttpStatusCode expected)
    {
        var client = await _factory.CreateAuthenticatedClientAsync(username, ApiFactory.DemoPassword);

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

        response.StatusCode.Should().Be(expected);
    }

    [Fact]
    public async Task StationScreens_ReceiveOnlyTheirTickets_WhileUnboundScreensSeeEverything()
    {
        var kitchen = await _factory.LoginAsync("kitchen1", ApiFactory.DemoPassword, deviceName: "KITCHEN-01");
        var kitchenClient = await _factory.CreateAuthenticatedClientAsync("kitchen1", ApiFactory.DemoPassword);
        var stations = (await (await kitchenClient.GetAsync("/api/stations")).ReadEnvelopeAsync<List<StationDto>>()).Data!;
        var main = stations.Single(s => s.Code == "MAIN").Id;

        await using var mainScreen = _factory.CreateHubConnection(kitchen.AccessToken);
        await using var allScreen = _factory.CreateHubConnection(kitchen.AccessToken);
        var mainSeen = new ConcurrentBag<int>();
        var allSeen = new ConcurrentBag<int>();
        mainScreen.On<KitchenTicketCreatedEvent>(HubEvents.KitchenTicketCreated, e => mainSeen.Add(e.StationId));
        allScreen.On<KitchenTicketCreatedEvent>(HubEvents.KitchenTicketCreated, e => allSeen.Add(e.StationId));
        await mainScreen.StartAsync();
        await allScreen.StartAsync();
        await mainScreen.InvokeAsync(HubMethods.JoinStation, main);

        var order = await SendOrderAsync("T11", "Chicken Biryani", "Fresh Lime Soda");

        await WaitUntilAsync(() => allSeen.Count >= 2 && !mainSeen.IsEmpty);
        await Task.Delay(300);
        mainSeen.Should().Equal(main);
        allSeen.Should().HaveCount(2);
        var tickets = (await (await kitchenClient.GetAsync("/api/kitchen/orders")).ReadEnvelopeAsync<KitchenTicketListDto>()).Data!;
        tickets.Tickets.Where(t => t.OrderId == order.Id).Select(t => t.StationCode).Should().BeEquivalentTo("MAIN", "BAR");
    }

    [Fact]
    public async Task ReadyTicket_NotifiesTheWaiter_AndTurnsTheTableReady()
    {
        var waiter = await _factory.LoginAsync("waiter1", ApiFactory.DemoPassword, deviceName: "WAITER-07");
        await using var waiterHub = _factory.CreateHubConnection(waiter.AccessToken);
        var ready = new TaskCompletionSource<OrderProgressEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        waiterHub.On<OrderProgressEvent>(HubEvents.OrderReady, e => ready.TrySetResult(e));
        await waiterHub.StartAsync();

        var order = await SendOrderAsync("T12", "Chicken Biryani");
        var kitchen = await _factory.CreateAuthenticatedClientAsync("kitchen1", ApiFactory.DemoPassword);
        var ticket = order.Tickets.Single();
        (await kitchen.PostAsync($"/api/kitchen/orders/{ticket.Id}/start", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await kitchen.PostAsync($"/api/kitchen/orders/{ticket.Id}/ready", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var notice = await ready.Task.WaitAsync(TimeSpan.FromSeconds(15));
        notice.TableCode.Should().Be("T12");
        notice.ReadyTicketNumbers.Should().Equal(ticket.TicketNumber);
        var waiterClient = await _factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword);
        var table = (await (await waiterClient.GetAsync($"/api/tables/{order.TableId}")).ReadEnvelopeAsync<TableDetailDto>()).Data!;
        table.Table.Status.Should().Be(TableStatus.Ready);

        var served = await waiterClient.PostAsync($"/api/orders/{order.Id}/serve", null);
        (await served.ReadEnvelopeAsync<OrderDetailDto>()).Data!.Status.Should().Be(OrderStatus.Served);
    }

    private async Task<OrderDetailDto> SendOrderAsync(string tableCode, params string[] items)
    {
        var waiter = await _factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword);
        var map = (await (await waiter.GetAsync("/api/tables")).ReadEnvelopeAsync<TableMapDto>()).Data!;
        var menu = (await (await waiter.GetAsync("/api/menu")).ReadEnvelopeAsync<MenuDto>()).Data!;
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
        {
            Content = JsonContent.Create(new CreateOrderRequest
            {
                TableId = map.Sections.SelectMany(s => s.Tables).Single(t => t.Code == tableCode).Id,
                GuestCount = 2,
                Submit = true,
                Items = items.Select(name =>
                {
                    var entry = menu.Items.Single(i => i.Name == name);
                    var spice = menu.ModifierGroups.Where(g => entry.ModifierGroupIds.Contains(g.Id) && g.MinSelections > 0)
                        .Select(g => g.Options.First().Id);
                    return new OrderItemInput { MenuItemId = entry.Id, Quantity = 1, ModifierOptionIds = spice.ToList() };
                }).ToList(),
            }, options: PosJson.Options),
        };
        request.Headers.Add(PosHeaders.IdempotencyKey, Guid.NewGuid().ToString());
        var envelope = await (await waiter.SendAsync(request)).ReadEnvelopeAsync<OrderDetailDto>();
        envelope.Success.Should().BeTrue(envelope.Message);
        return envelope.Data!;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(100);
        }
    }
}
