using System.Net;
using System.Net.Http.Json;
using HotelPOS.Api.Common;
using HotelPOS.Api.Tests.Support;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Kitchen;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Orders;
using HotelPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HotelPOS.Api.Tests;

// Client KOT features: stock counts, parcel packaging, seats, table merge.
public class Phase27ApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public Phase27ApiTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ExactStock_CountsDownOnProcess_RefusesTooMany_SellsOutAndComesBackOnCancel()
    {
        var waiter = await _factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword);
        var kitchen = await _factory.CreateAuthenticatedClientAsync("kitchen1", ApiFactory.DemoPassword);
        var table = await TableAsync(waiter, "T01");
        var item = (await MenuAsync(waiter)).Items.First(i => i.Name == "Butter Naan");

        var set = await kitchen.PatchAsJsonAsync($"/api/menu-items/{item.Id}/stock",
            new SetStockRequest { Mode = StockTrackingMode.Exact, Quantity = 3 }, PosJson.Options);
        set.StatusCode.Should().Be(HttpStatusCode.OK);

        var order = await PostAsync<OrderDetailDto>(waiter, "/api/orders", new CreateOrderRequest
        {
            TableId = table.Id, GuestCount = 2, Submit = true,
            Items = new[] { new OrderItemInput { MenuItemId = item.Id, Quantity = 2 } },
        });
        (await StockAsync(waiter, item.Id)).Should().Be(1);

        var tooMany = await SendAsync(waiter, $"/api/orders/{order.Id}/items", new AppendOrderItemsRequest
        {
            Items = new[] { new OrderItemInput { MenuItemId = item.Id, Quantity = 2 } },
        });
        tooMany.StatusCode.Should().NotBe(HttpStatusCode.OK);
        (await StockAsync(waiter, item.Id)).Should().Be(1, "a refused order must leave the count untouched");

        await PostAsync<OrderDetailDto>(waiter, $"/api/orders/{order.Id}/items", new AppendOrderItemsRequest
        {
            Items = new[] { new OrderItemInput { MenuItemId = item.Id, Quantity = 1 } },
        });
        var soldOut = (await MenuAsync(waiter)).Items.Single(i => i.Id == item.Id);
        soldOut.StockRemaining.Should().Be(0);
        soldOut.IsAvailable.Should().BeFalse("an item at zero is switched off on every captain's menu");

        var latest = (await (await waiter.GetAsync($"/api/orders/{order.Id}")).ReadEnvelopeAsync<OrderDetailDto>()).Data!;
        var line = latest.Items.Last(i => i.MenuItemId == item.Id);
        var cancel = await SendAsync(waiter, $"/api/orders/{order.Id}/items/{line.Id}/cancel", new CancelOrderItemRequest { Reason = "Guest changed mind" });
        cancel.StatusCode.Should().Be(HttpStatusCode.OK);

        var back = (await MenuAsync(waiter)).Items.Single(i => i.Id == item.Id);
        back.StockRemaining.Should().Be(1);
        back.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task TwoCaptainsOrderingTheLastPortionAtOnce_OneGetsItAndTheOtherIsTold()
    {
        var waiter = await _factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword);
        var kitchen = await _factory.CreateAuthenticatedClientAsync("kitchen1", ApiFactory.DemoPassword);
        var first = await TableAsync(waiter, "T08");
        var second = await TableAsync(waiter, "T11");
        var item = (await MenuAsync(waiter)).Items.Last(i => i.Name != "Butter Naan" && i.IsAvailable && i.ModifierGroupIds.Count == 0);
        (await kitchen.PatchAsJsonAsync($"/api/menu-items/{item.Id}/stock",
            new SetStockRequest { Mode = StockTrackingMode.Exact, Quantity = 1 }, PosJson.Options)).StatusCode.Should().Be(HttpStatusCode.OK);

        Task<HttpResponseMessage> Order(TableDto table) => SendAsync(waiter, "/api/orders", new CreateOrderRequest
        {
            TableId = table.Id, GuestCount = 2, Submit = true,
            Items = new[] { new OrderItemInput { MenuItemId = item.Id, Quantity = 1 } },
        });

        var results = await Task.WhenAll(Order(first), Order(second));

        results.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        results.Count(r => r.StatusCode != HttpStatusCode.Created).Should().Be(1);
        (await StockAsync(waiter, item.Id)).Should().Be(0);
    }

    [Fact]
    public async Task Parcel_AddsPackagingChargeToThePriceAndMarksTheName()
    {
        var waiter = await _factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword);
        var table = await TableAsync(waiter, "T02");
        var item = (await MenuAsync(waiter)).Items.First(i => i.Name != "Butter Naan" && i.IsAvailable && i.ModifierGroupIds.Count == 0);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().MenuItems
                .Where(m => m.Id == item.Id).ExecuteUpdateAsync(s => s.SetProperty(m => m.PackagingCharge, 10m));
        }

        var order = await PostAsync<OrderDetailDto>(waiter, "/api/orders", new CreateOrderRequest
        {
            TableId = table.Id, GuestCount = 1, Submit = true,
            Items = new[]
            {
                new OrderItemInput { MenuItemId = item.Id, Quantity = 1, IsParcel = true },
                new OrderItemInput { MenuItemId = item.Id, Quantity = 1 },
            },
        });

        var parcel = order.Items.Single(i => i.IsParcel);
        parcel.UnitPrice.Should().Be(item.Price + 10m);
        parcel.PackagingCharge.Should().Be(10m);
        parcel.ItemName.Should().EndWith("(Parcel)");
        order.Items.Single(i => !i.IsParcel).UnitPrice.Should().Be(item.Price);
    }

    [Fact]
    public async Task DefaultPackingCharge_AppliesToParcelsOfItemsWithoutTheirOwn_AndReachesTheMenu()
    {
        var admin = await _factory.CreateAuthenticatedClientAsync("admin", ApiFactory.AdminPassword);
        var waiter = await _factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword);
        var table = await TableAsync(waiter, "T06");
        int itemId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            itemId = await db.MenuItems.Where(m => m.IsActive && m.IsAvailable && m.PackagingCharge == 0m && m.StockMode == StockTrackingMode.None)
                .OrderByDescending(m => m.Id).Select(m => m.Id).FirstAsync();
        }

        var save = await admin.PutAsJsonAsync("/api/admin/settings",
            new UpdateSettingsRequest { Items = new[] { new SettingValue { Key = SettingKeys.DefaultPackagingCharge, Value = "7.50" } } }, PosJson.Options);
        save.StatusCode.Should().Be(HttpStatusCode.OK);

        var menuItem = (await MenuAsync(waiter)).Items.Single(i => i.Id == itemId);
        menuItem.PackagingCharge.Should().Be(7.5m, "terminals preview the parcel price from the menu");

        var order = await PostAsync<OrderDetailDto>(waiter, "/api/orders", new CreateOrderRequest
        {
            TableId = table.Id, GuestCount = 1, Submit = true,
            Items = new[] { new OrderItemInput { MenuItemId = itemId, Quantity = 1, IsParcel = true } },
        });
        order.Items.Single().PackagingCharge.Should().Be(7.5m);
        order.Items.Single().UnitPrice.Should().Be(menuItem.Price + 7.5m);

        var tooMuch = await admin.PutAsJsonAsync("/api/admin/settings",
            new UpdateSettingsRequest { Items = new[] { new SettingValue { Key = SettingKeys.DefaultPackagingCharge, Value = "-1" } } }, PosJson.Options);
        tooMuch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Seats_AreLimitedByTheTableCapacity()
    {
        var waiter = await _factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword);
        var table = await TableAsync(waiter, "T03");
        table.SeatCodes.Should().Equal("T03A", "T03B", "T03C", "T03D");
        var item = (await MenuAsync(waiter)).Items.First(i => i.Name != "Butter Naan" && i.IsAvailable && i.ModifierGroupIds.Count == 0);

        var bad = await SendAsync(waiter, "/api/orders", new CreateOrderRequest
        {
            TableId = table.Id, GuestCount = 2,
            Items = new[] { new OrderItemInput { MenuItemId = item.Id, SeatCode = "T03E" } },
        });
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var order = await PostAsync<OrderDetailDto>(waiter, "/api/orders", new CreateOrderRequest
        {
            TableId = table.Id, GuestCount = 2,
            Items = new[] { new OrderItemInput { MenuItemId = item.Id, SeatCode = "t03b" } },
        });
        order.Items.Single().SeatCode.Should().Be("T03B");
    }

    [Fact]
    public async Task Merge_LocksTheCompanion_BillsOnOnePrimaryOrder_AndFreesBothWhenThePartyLeaves()
    {
        var waiter = await _factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword);
        var primary = await TableAsync(waiter, "T04");
        var companion = await TableAsync(waiter, "T05");
        var item = (await MenuAsync(waiter)).Items.First(i => i.Name != "Butter Naan" && i.IsAvailable && i.ModifierGroupIds.Count == 0);

        // Merge needs seated guests first.
        var tooEarly = await SendAsync(waiter, $"/api/tables/{primary.Id}/merge", new MergeTablesRequest { CompanionTableId = companion.Id });
        tooEarly.StatusCode.Should().NotBe(HttpStatusCode.OK);

        var occupied = await PostAsync<TableDto>(waiter, $"/api/tables/{primary.Id}/occupy", new OccupyTableRequest { GuestCount = 8, RowVersion = primary.RowVersion });
        occupied.Status.Should().Be(TableStatus.Occupied);

        var merged = await SendAsync(waiter, $"/api/tables/{primary.Id}/merge", new MergeTablesRequest { CompanionTableId = companion.Id });
        merged.StatusCode.Should().Be(HttpStatusCode.OK);

        var map = await TableAsync(waiter, "T05");
        map.MergedIntoTableId.Should().Be(primary.Id);
        map.MergedIntoTableCode.Should().Be("T04");
        (await TableAsync(waiter, "T04")).MergedTableCodes.Should().Equal("T05");

        // The companion cannot be opened.
        var blocked = await SendAsync(waiter, "/api/orders", new CreateOrderRequest { TableId = companion.Id, GuestCount = 2 });
        blocked.StatusCode.Should().NotBe(HttpStatusCode.Created);

        // Seats of both tables can be used on the primary's order.
        var order = await PostAsync<OrderDetailDto>(waiter, "/api/orders", new CreateOrderRequest
        {
            TableId = primary.Id, GuestCount = 8, Submit = true,
            Items = new[]
            {
                new OrderItemInput { MenuItemId = item.Id, SeatCode = "T04A" },
                new OrderItemInput { MenuItemId = item.Id, SeatCode = "T05D" },
            },
        });
        order.Items.Select(i => i.SeatCode).Should().BeEquivalentTo("T04A", "T05D");

        // Cancel the order, release the primary (guests leave): the companion is freed with it.
        var cancel = await SendAsync(waiter, $"/api/orders/{order.Id}/cancel", new CancelOrderRequest { Reason = "Party left" });
        cancel.StatusCode.Should().Be(HttpStatusCode.OK);
        var release = await SendAsync(waiter, $"/api/tables/{primary.Id}/release", new { });
        release.StatusCode.Should().Be(HttpStatusCode.OK);

        var freed = await TableAsync(waiter, "T05");
        freed.Status.Should().Be(TableStatus.Available);
        freed.MergedIntoTableId.Should().BeNull();
    }

    [Fact]
    public async Task Unmerge_FreesTheCompanionAtOnce()
    {
        var waiter = await _factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword);
        var primary = await TableAsync(waiter, "T06");
        var companion = await TableAsync(waiter, "T07");
        await PostAsync<TableDto>(waiter, $"/api/tables/{primary.Id}/occupy", new OccupyTableRequest { GuestCount = 6, RowVersion = primary.RowVersion });
        (await SendAsync(waiter, $"/api/tables/{primary.Id}/merge", new MergeTablesRequest { CompanionTableId = companion.Id })).StatusCode.Should().Be(HttpStatusCode.OK);

        (await SendAsync(waiter, $"/api/tables/{companion.Id}/unmerge", new { })).StatusCode.Should().Be(HttpStatusCode.OK);

        var freed = await TableAsync(waiter, "T07");
        freed.Status.Should().Be(TableStatus.Available);
        (await TableAsync(waiter, "T06")).MergedTableCodes.Should().BeEmpty();
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string url, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body, body.GetType(), options: PosJson.Options) };
        request.Headers.Add(PosHeaders.IdempotencyKey, Guid.NewGuid().ToString());
        return await client.SendAsync(request);
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object body)
    {
        var response = await SendAsync(client, url, body);
        var envelope = await response.ReadEnvelopeAsync<T>();
        envelope.Success.Should().BeTrue(envelope.Message);
        return envelope.Data!;
    }

    private static async Task<MenuDto> MenuAsync(HttpClient client) =>
        (await (await client.GetAsync("/api/menu")).ReadEnvelopeAsync<MenuDto>()).Data!;

    private static async Task<int?> StockAsync(HttpClient client, int itemId) =>
        (await MenuAsync(client)).Items.Single(i => i.Id == itemId).StockRemaining;

    private static async Task<TableDto> TableAsync(HttpClient client, string code) =>
        (await (await client.GetAsync("/api/tables")).ReadEnvelopeAsync<TableMapDto>()).Data!
        .Sections.SelectMany(s => s.Tables).Single(t => t.Code == code);
}
