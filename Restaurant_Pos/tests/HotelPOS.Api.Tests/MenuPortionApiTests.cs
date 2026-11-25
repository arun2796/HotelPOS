using System.Net;
using System.Net.Http.Json;
using HotelPOS.Api.Tests.Support;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Menu;
using HotelPOS.Testing;

namespace HotelPOS.Api.Tests;

public class MenuPortionApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public MenuPortionApiTests(ApiFactory factory)
    {
        _factory = factory;
    }

    private static List<SaveMenuItemPortionRequest> Portions() => new()
    {
        new() { Name = "Quarter", Quantity = 250, Unit = "GM", Price = 100m, SortOrder = 1 },
        new() { Name = "Half", Quantity = 500, Unit = "GM", Price = 180m, IsDefault = true, SortOrder = 2 },
        new() { Name = "Full", Price = 320m, SortOrder = 3 },
    };

    private async Task<CreateMenuItemRequest> NewItemAsync(HttpClient client, string name)
    {
        var menu = (await (await client.GetAsync("/api/menu")).ReadEnvelopeAsync<MenuDto>()).Data!;
        var template = menu.Items.First();
        return new CreateMenuItemRequest
        {
            CategoryId = template.CategoryId,
            Name = name,
            Price = 1m,
            TaxId = template.TaxId,
            PreparationStationId = template.StationId,
            Portions = Portions(),
        };
    }

    [Fact]
    public async Task PortionsRoundTripThroughTheMenuEndpoints()
    {
        var manager = await _factory.CreateAuthenticatedClientAsync("manager1", ApiFactory.DemoPassword);
        var waiter = await _factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword);

        var created = await manager.PostAsJsonAsync("/api/menu-items", await NewItemAsync(manager, "Portion Biryani"), PosJson.Options);

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var item = (await created.ReadEnvelopeAsync<MenuItemDto>()).Data!;
        item.Price.Should().Be(180m);
        item.Portions.Select(p => p.Name).Should().Equal("Quarter", "Half", "Full");

        var menu = (await (await waiter.GetAsync("/api/menu")).ReadEnvelopeAsync<MenuDto>()).Data!;
        var entry = menu.Items.Single(i => i.Id == item.Id);
        entry.Portions.Select(p => (p.Name, p.Price, p.IsDefault)).Should().Equal(("Half", 180m, true), ("Quarter", 100m, false), ("Full", 320m, false));

        var rows = item.Portions.Select(p => new SaveMenuItemPortionRequest
        {
            Id = p.Id, Name = p.Name, Quantity = p.Quantity, Unit = p.Unit, Price = p.Name == "Half" ? 200m : p.Price,
            IsDefault = p.IsDefault, IsActive = p.IsActive, SortOrder = p.SortOrder,
        }).ToList();
        var updated = await manager.PutAsJsonAsync($"/api/menu-items/{item.Id}", new UpdateMenuItemRequest
        {
            CategoryId = item.CategoryId, Name = item.Name, Price = 1m, TaxId = item.TaxId, PreparationStationId = item.PreparationStationId,
            ModifierGroupIds = item.ModifierGroupIds, RowVersion = item.RowVersion, Portions = rows,
        }, PosJson.Options);

        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = (await updated.ReadEnvelopeAsync<MenuItemDto>()).Data!;
        after.Price.Should().Be(200m);
        after.Portions.Single(p => p.Name == "Half").Id.Should().Be(item.Portions.Single(p => p.Name == "Half").Id);
    }

    [Fact]
    public async Task ABadPortionList_Is4xxWithAMessage()
    {
        var manager = await _factory.CreateAuthenticatedClientAsync("manager1", ApiFactory.DemoPassword);
        var request = await NewItemAsync(manager, "Broken Biryani");

        var response = await manager.PostAsJsonAsync("/api/menu-items", request with
        {
            Portions = new List<SaveMenuItemPortionRequest> { new() { Name = "A", Price = 1m, IsDefault = true }, new() { Name = "B", Price = 2m, IsDefault = true } },
        }, PosJson.Options);

        ((int)response.StatusCode).Should().BeInRange(400, 422);
        (await response.Content.ReadAsStringAsync()).Should().Contain("exactly one default");
    }

    [Fact]
    public async Task WaiterAndCashierCannotEditPortions()
    {
        var manager = await _factory.CreateAuthenticatedClientAsync("manager1", ApiFactory.DemoPassword);
        var body = await NewItemAsync(manager, "Locked Biryani");

        foreach (var user in new[] { "waiter1", "cashier1", "kitchen1" })
        {
            var client = await _factory.CreateAuthenticatedClientAsync(user, ApiFactory.DemoPassword);
            (await client.PostAsJsonAsync("/api/menu-items", body, PosJson.Options)).StatusCode.Should().Be(HttpStatusCode.Forbidden, user);
        }
    }

    [Fact]
    public async Task OldClients_ThatSendOnlyAPrice_StillCreateAndEditItems()
    {
        var manager = await _factory.CreateAuthenticatedClientAsync("manager1", ApiFactory.DemoPassword);
        var request = (await NewItemAsync(manager, "Old Client Dish")) with { Price = 95m, Portions = null };

        var created = await manager.PostAsJsonAsync("/api/menu-items", request, PosJson.Options);

        var item = (await created.ReadEnvelopeAsync<MenuItemDto>()).Data!;
        item.Portions.Should().ContainSingle().Which.Should().Match<MenuItemPortionDto>(p => p.Name == "Regular" && p.Price == 95m);

        var updated = await manager.PutAsJsonAsync($"/api/menu-items/{item.Id}", new UpdateMenuItemRequest
        {
            CategoryId = item.CategoryId, Name = item.Name, Price = 105m, TaxId = item.TaxId, PreparationStationId = item.PreparationStationId,
            RowVersion = item.RowVersion,
        }, PosJson.Options);

        var after = (await updated.ReadEnvelopeAsync<MenuItemDto>()).Data!;
        after.Price.Should().Be(105m);
        after.Portions.Should().ContainSingle().Which.Price.Should().Be(105m);
    }
}
