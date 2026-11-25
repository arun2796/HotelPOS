using System.Net;
using System.Net.Http.Json;
using HotelPOS.Api.Tests.Support;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Orders;
using HotelPOS.Testing;

namespace HotelPOS.Api.Tests;

public class PortionOrderApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public PortionOrderApiTests(ApiFactory factory)
    {
        _factory = factory;
    }

    private async Task<MenuItemDto> CreatePortionedItemAsync(string name)
    {
        var manager = await _factory.CreateAuthenticatedClientAsync("manager1", ApiFactory.DemoPassword);
        var template = (await (await manager.GetAsync("/api/menu")).ReadEnvelopeAsync<MenuDto>()).Data!.Items.First(i => i.ModifierGroupIds.Count == 0);
        var response = await manager.PostAsJsonAsync("/api/menu-items", new CreateMenuItemRequest
        {
            CategoryId = template.CategoryId, Name = name, Price = 1m, TaxId = template.TaxId, PreparationStationId = template.StationId,
            Portions = new List<SaveMenuItemPortionRequest>
            {
                new() { Name = "Half", Quantity = 500, Unit = "GM", Price = 180m, IsDefault = true, SortOrder = 1 },
                new() { Name = "Full", Price = 320m, SortOrder = 2 },
            },
        }, PosJson.Options);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.ReadEnvelopeAsync<MenuItemDto>()).Data!;
    }

    private async Task<(HttpClient Waiter, int TableId)> WaiterAndTableAsync(string tableCode)
    {
        var waiter = await _factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword);
        var map = (await (await waiter.GetAsync("/api/tables")).ReadEnvelopeAsync<TableMapDto>()).Data!;
        return (waiter, map.Sections.SelectMany(s => s.Tables).Single(t => t.Code == tableCode).Id);
    }

    private static HttpRequestMessage Create(int tableId, params OrderItemInput[] items)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
        {
            Content = JsonContent.Create(new CreateOrderRequest { TableId = tableId, GuestCount = 2, Submit = true, Items = items }, options: PosJson.Options),
        };
        request.Headers.Add(PosHeaders.IdempotencyKey, Guid.NewGuid().ToString());
        return request;
    }

    [Fact]
    public async Task TwoPortionsOfOneItem_AreTwoLines_FrozenWithTheirPortion()
    {
        var item = await CreatePortionedItemAsync("API Biryani");
        var (waiter, tableId) = await WaiterAndTableAsync("T08");
        var half = item.Portions.Single(p => p.Name == "Half").Id;
        var full = item.Portions.Single(p => p.Name == "Full").Id;

        var response = await waiter.SendAsync(Create(tableId,
            new OrderItemInput { MenuItemId = item.Id, Quantity = 1, PortionId = half },
            new OrderItemInput { MenuItemId = item.Id, Quantity = 2, PortionId = full }));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var order = (await response.ReadEnvelopeAsync<OrderDetailDto>()).Data!;
        order.Items.Select(i => (i.DisplayName, i.Quantity, i.UnitPrice, i.PortionName)).Should().Equal(
            ("API Biryani (Half)", 1, 180m, "Half"), ("API Biryani (Full)", 2, 320m, "Full"));
        order.ApproxSubtotal.Should().Be(820m);

        var reloaded = (await (await waiter.GetAsync($"/api/orders/{order.Id}")).ReadEnvelopeAsync<OrderDetailDto>()).Data!;
        reloaded.Items.Select(i => i.DisplayName).Should().Equal("API Biryani (Half)", "API Biryani (Full)");
    }

    [Fact]
    public async Task AnOrderLineWithoutAPortion_OnAnItemWithTwoPortions_IsRefusedWithAMessage()
    {
        var item = await CreatePortionedItemAsync("API Pulao");
        var (waiter, tableId) = await WaiterAndTableAsync("T07");

        var response = await waiter.SendAsync(Create(tableId, new OrderItemInput { MenuItemId = item.Id, Quantity = 1 }));

        ((int)response.StatusCode).Should().BeInRange(400, 422);
        (await response.Content.ReadAsStringAsync()).Should().Contain("has several portions");
    }

    [Fact]
    public async Task AnOldClientOrderingAnItemWithOnePrice_StillWorks()
    {
        var (waiter, tableId) = await WaiterAndTableAsync("T06");
        var menu = (await (await waiter.GetAsync("/api/menu")).ReadEnvelopeAsync<MenuDto>()).Data!;
        var single = menu.Items.First(i => i.Portions.Count == 1 && i.ModifierGroupIds.Count == 0 && i.IsAvailable);

        var response = await waiter.SendAsync(Create(tableId, new OrderItemInput { MenuItemId = single.Id, Quantity = 1 }));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var line = (await response.ReadEnvelopeAsync<OrderDetailDto>()).Data!.Items.Single();
        (line.UnitPrice, line.PortionName, line.DisplayName).Should().Be((single.Price, (string?)null, single.Name));
    }
}
