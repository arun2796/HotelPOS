using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using HotelPOS.Api.Controllers;
using HotelPOS.Api.Tests.Support;
using HotelPOS.Contracts.Auth;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Kitchen;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Orders;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace HotelPOS.Api.Tests;

/// <summary>
/// Phase 18.5 integration: the browser clients and the desktop share one backend. This drives the same routes the
/// web pages call — browser sign-in, table map, occupy, create + submit with an Idempotency-Key (and its replay),
/// kitchen list and accept, order status back to the waiter — with nothing but the existing API.
/// </summary>
public class WebFlowTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public WebFlowTests(ApiFactory factory)
    {
        _factory = factory;
        _factory.ResetRateLimiter();
    }

    [Fact]
    public async Task WaiterPhone_OrdersThroughTheWebSignIn_AndTheKitchenTabletSeesTheTicket()
    {
        using var waiter = await WebClientAsync("waiter1", "WEB-WAITER-FLOW01", DeviceType.Waiter);
        using var kitchen = await WebClientAsync("kitchen1", "WEB-KITCHEN-FLOW01", DeviceType.Kitchen);

        // Table map and menu, as the Tables and Order Builder pages load them.
        var map = (await (await waiter.GetAsync("/api/tables")).ReadEnvelopeAsync<TableMapDto>()).Data!;
        var table = map.Sections.SelectMany(s => s.Tables).First(t => t.IsActive && t.Status == TableStatus.Available);
        var menu = (await (await waiter.GetAsync("/api/menu")).ReadEnvelopeAsync<MenuDto>()).Data!;
        var item = menu.Items.First(i => i.IsAvailable && i.ModifierGroupIds.Count == 0);

        // Occupy, then create + submit in one call with a key, exactly as the web Order Builder sends it.
        var occupied = await waiter.PostAsJsonAsync($"/api/tables/{table.Id}/occupy", new OccupyTableRequest { GuestCount = 2, RowVersion = table.RowVersion }, PosJson.Options);
        occupied.StatusCode.Should().Be(HttpStatusCode.OK);

        var key = Guid.NewGuid();
        var request = new CreateOrderRequest { TableId = table.Id, GuestCount = 2, Submit = true, Items = new[] { new OrderItemInput { MenuItemId = item.Id, Quantity = 2, Notes = "Less oil" } } };
        var first = await SendAsync(waiter, "/api/orders", request, key);
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var order = (await first.ReadEnvelopeAsync<OrderDetailDto>()).Data!;
        order.Status.Should().Be(OrderStatus.Submitted);
        order.Tickets.Should().NotBeEmpty();

        // The phone retried after a dropped connection: same key → replayed answer, no second order.
        var replay = await SendAsync(waiter, "/api/orders", request, key);
        replay.Headers.Contains("Idempotent-Replayed").Should().BeTrue();
        (await replay.ReadEnvelopeAsync<OrderDetailDto>()).Data!.Id.Should().Be(order.Id);

        // The kitchen tablet lists the ticket for its station and accepts it.
        var ticket = order.Tickets[0];
        var listed = (await (await kitchen.GetAsync($"/api/kitchen/orders?stationId={item.StationId}")).ReadEnvelopeAsync<KitchenTicketListDto>()).Data!;
        listed.Tickets.Should().Contain(t => t.Id == ticket.Id);
        var accepted = await kitchen.PostAsync($"/api/kitchen/orders/{ticket.Id}/accept", null);
        accepted.StatusCode.Should().Be(HttpStatusCode.OK);

        // The waiter's Table details page reloads the order and sees the kitchen's progress.
        var reloaded = (await (await waiter.GetAsync($"/api/orders/{order.Id}")).ReadEnvelopeAsync<OrderDetailDto>()).Data!;
        reloaded.Tickets.Should().Contain(t => t.Id == ticket.Id && t.Status == KitchenOrderStatus.Accepted);

        // A kitchen user cannot do waiter work and vice versa — the server decides, not the device type.
        (await kitchen.PostAsJsonAsync("/api/orders", request, PosJson.Options)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await waiter.PostAsync($"/api/kitchen/orders/{ticket.Id}/start", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<HttpClient> WebClientAsync(string username, string deviceName, DeviceType type)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var login = await client.PostAsJsonAsync("/api/auth/web/login",
            new LoginRequest { Username = username, Password = ApiFactory.DemoPassword, DeviceName = deviceName, DeviceType = type }, PosJson.Options);
        var body = await login.ReadEnvelopeAsync<LoginResponse>();
        body.Success.Should().BeTrue(body.Message);
        body.Data!.RefreshToken.Should().BeEmpty();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.Data.AccessToken);
        return client;
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string url, object body, Guid key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body, options: PosJson.Options) };
        request.Headers.Add(PosHeaders.IdempotencyKey, key.ToString());
        return client.SendAsync(request);
    }
}
