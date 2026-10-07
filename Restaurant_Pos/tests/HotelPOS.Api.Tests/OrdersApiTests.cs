using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using HotelPOS.Api.Common;
using HotelPOS.Api.Tests.Support;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Orders;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Infrastructure.Persistence;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HotelPOS.Api.Tests;

public class OrdersApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public OrdersApiTests(ApiFactory factory)
    {
        _factory = factory;
    }

    public static TheoryData<string, string, string, HttpStatusCode> Matrix => new()
    {
        { "kitchen1", "GET", "/api/orders/active", HttpStatusCode.OK },
        { "cashier1", "GET", "/api/orders", HttpStatusCode.OK },
        { "cashier1", "POST", "/api/orders", HttpStatusCode.Forbidden },
        { "kitchen1", "POST", "/api/orders/1/cancel", HttpStatusCode.Forbidden },
        { "kitchen1", "POST", "/api/orders/1/items", HttpStatusCode.Forbidden },
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
    public async Task CreateSubmitAppendCancel_PublishesEveryEventToOtherTerminals()
    {
        var manager = await _factory.LoginAsync("manager1", ApiFactory.DemoPassword, deviceName: "ADMIN-04");
        await using var hub = _factory.CreateHubConnection(manager.AccessToken);
        var events = new ConcurrentQueue<string>();
        hub.On<OrderCreatedEvent>(HubEvents.OrderCreated, e => events.Enqueue($"Created #{e.OrderNumber} {e.TableCode} {e.ItemCount}"));
        hub.On<OrderUpdatedEvent>(HubEvents.OrderUpdated, e => events.Enqueue($"Updated {e.ChangeType}"));
        hub.On<OrderCancelledEvent>(HubEvents.OrderCancelled, e => events.Enqueue($"Cancelled {e.Reason}"));
        hub.On<TableStatusChangedEvent>(HubEvents.TableStatusChanged, e => events.Enqueue($"Table {e.TableCode} {e.Status}"));
        await hub.StartAsync();

        var waiter = await _factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword);
        var table = await FindTableAsync(waiter, "T02");
        var menu = await MenuAsync(waiter);
        var naan = menu.Items.First(i => i.Name == "Butter Naan");

        var created = await PostAsync<OrderDetailDto>(waiter, "/api/orders", new CreateOrderRequest
        {
            TableId = table.Id,
            GuestCount = 2,
            Items = new[] { new OrderItemInput { MenuItemId = naan.Id, Quantity = 2 } },
            Submit = true,
        }, Guid.NewGuid());
        await PostAsync<OrderDetailDto>(waiter, $"/api/orders/{created.Id}/items",
            new AppendOrderItemsRequest { Items = new[] { new OrderItemInput { MenuItemId = naan.Id, Quantity = 1 } } }, Guid.NewGuid());
        var cancel = await waiter.PostAsJsonAsync($"/api/orders/{created.Id}/cancel", new CancelOrderRequest { Reason = "Guests left" }, PosJson.Options);
        cancel.StatusCode.Should().Be(HttpStatusCode.OK);

        await WaitUntilAsync(() => events.Count >= 5);
        events.Should().ContainInOrder(
            $"Table T02 {TableStatus.Preparing}",
            $"Created #{created.OrderNumber} T02 2",
            $"Updated {OrderChangeTypes.ItemsAppended}",
            "Cancelled Guests left",
            $"Table T02 {TableStatus.Available}");
    }

    [Fact]
    public async Task Create_RetriedWithTheSameKey_ReturnsTheSameOrder_AndStoresOneRow()
    {
        var waiter = await _factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword);
        var table = await FindTableAsync(waiter, "T03");
        var naan = (await MenuAsync(waiter)).Items.First(i => i.Name == "Butter Naan");
        var request = new CreateOrderRequest
        {
            TableId = table.Id,
            GuestCount = 4,
            Items = new[] { new OrderItemInput { MenuItemId = naan.Id, Quantity = 3 } },
            Submit = true,
        };
        var key = Guid.NewGuid();

        var first = await SendAsync(waiter, "/api/orders", request, key);
        var retry = await SendAsync(waiter, "/api/orders", request, key);
        var reused = await SendAsync(waiter, "/api/orders", request with { GuestCount = 5 }, key);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        retry.StatusCode.Should().Be(HttpStatusCode.Created);
        retry.Headers.GetValues(IdempotencyMiddleware.ReplayedHeader).Should().Equal("true");
        (await retry.ReadEnvelopeAsync<OrderDetailDto>()).Data!.Id.Should().Be((await first.ReadEnvelopeAsync<OrderDetailDto>()).Data!.Id);
        reused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await reused.ReadEnvelopeAsync<object>()).Errors.Single().Code.Should().Be(ErrorCodes.IdempotencyKeyReused);
        (await CountOrdersAsync(table.Id)).Should().Be(1);
    }

    [Fact]
    public async Task Create_WithoutAnIdempotencyKey_Returns400()
    {
        var waiter = await _factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword);
        var table = await FindTableAsync(waiter, "T04");

        var response = await waiter.PostAsJsonAsync("/api/orders", new CreateOrderRequest { TableId = table.Id, GuestCount = 2 }, PosJson.Options);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CountOrdersAsync(table.Id)).Should().Be(0);
    }

    [Fact]
    public async Task Cancel_OfAnOrderBeingPrepared_IsForbiddenForTheWaiter()
    {
        var waiter = await _factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword);
        var table = await FindTableAsync(waiter, "T06");
        var naan = (await MenuAsync(waiter)).Items.First(i => i.Name == "Butter Naan");
        var order = await PostAsync<OrderDetailDto>(waiter, "/api/orders", new CreateOrderRequest
        {
            TableId = table.Id,
            GuestCount = 2,
            Items = new[] { new OrderItemInput { MenuItemId = naan.Id, Quantity = 1 } },
            Submit = true,
        }, Guid.NewGuid());
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Orders
                .Where(o => o.Id == order.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Preparing));
        }

        var response = await waiter.PostAsJsonAsync($"/api/orders/{order.Id}/cancel", new CancelOrderRequest { Reason = "Too slow" }, PosJson.Options);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string url, object body, Guid key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body, body.GetType(), options: PosJson.Options) };
        request.Headers.Add(PosHeaders.IdempotencyKey, key.ToString());
        return await client.SendAsync(request);
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object body, Guid key)
    {
        var response = await SendAsync(client, url, body, key);
        var envelope = await response.ReadEnvelopeAsync<T>();
        envelope.Success.Should().BeTrue(envelope.Message);
        return envelope.Data!;
    }

    private async Task<int> CountOrdersAsync(int tableId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Orders.CountAsync(o => o.TableId == tableId);
    }

    private static async Task<MenuDto> MenuAsync(HttpClient client) =>
        (await (await client.GetAsync("/api/menu")).ReadEnvelopeAsync<MenuDto>()).Data!;

    private static async Task<TableDto> FindTableAsync(HttpClient client, string code) =>
        (await (await client.GetAsync("/api/tables")).ReadEnvelopeAsync<TableMapDto>()).Data!
        .Sections.SelectMany(s => s.Tables).Single(t => t.Code == code);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(100);
        }
    }
}
