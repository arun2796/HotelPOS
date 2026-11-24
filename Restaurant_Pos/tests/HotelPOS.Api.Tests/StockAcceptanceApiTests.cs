using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using HotelPOS.Api.Tests.Support;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Orders;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Users;
using Microsoft.AspNetCore.SignalR.Client;

namespace HotelPOS.Api.Tests;

// The client's own scenario, on an isolated database: Idli 40, Biryani 50, Grill 4, five roles looking at the same counts.
public class StockAcceptanceApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public StockAcceptanceApiTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task TheClientsScenario_IdliBiryaniGrill_AllFiveRolesSeeTheSameCounts_AtEveryStep()
    {
        var admin = await _factory.CreateAuthenticatedClientAsync("admin", ApiFactory.AdminPassword);
        var roles = new Dictionary<string, HttpClient>
        {
            ["Admin"] = admin,
            ["Manager"] = await _factory.CreateAuthenticatedClientAsync("manager1", ApiFactory.DemoPassword),
            ["Waiter"] = await _factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword),
            ["Cashier"] = await _factory.CreateAuthenticatedClientAsync("cashier1", ApiFactory.DemoPassword),
            ["Kitchen"] = await _factory.CreateAuthenticatedClientAsync("kitchen1", ApiFactory.DemoPassword),
        };
        var kitchen = roles["Kitchen"];
        var waiter1 = roles["Waiter"];

        // a second waiter (part of the demo staff)
        var waiter2 = await _factory.CreateAuthenticatedClientAsync("waiter2", ApiFactory.DemoPassword);

        // the three dishes, created by the admin
        var existing = (await Get<MenuDto>(waiter1, "/api/menu")).Items.First();
        async Task<MenuEntryDto> CreateDishAsync(string name, decimal price)
        {
            var response = await admin.PostAsJsonAsync("/api/menu-items", new CreateMenuItemRequest
            {
                CategoryId = existing.CategoryId, Name = name, Price = price, TaxId = existing.TaxId, PreparationStationId = existing.StationId,
            }, PosJson.Options);
            response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
            return (await Get<MenuDto>(waiter1, "/api/menu")).Items.Single(i => i.Name == name);
        }

        var idli = await CreateDishAsync("Idli", 20m);
        var biryani = await CreateDishAsync("Biryani Portion", 180m);
        var grill = await CreateDishAsync("Grill", 450m);

        // a screen for every role listens for stock changes
        var changes = new ConcurrentDictionary<string, int>();
        var screens = new List<HubConnection>();
        foreach (var (role, user, password) in new[] { ("Admin", "admin", ApiFactory.AdminPassword), ("Manager", "manager1", ApiFactory.DemoPassword), ("Waiter", "waiter1", ApiFactory.DemoPassword), ("Cashier", "cashier1", ApiFactory.DemoPassword), ("Kitchen", "kitchen1", ApiFactory.DemoPassword) })
        {
            var login = await _factory.LoginAsync(user, password, deviceName: "STOCK-" + role.ToUpperInvariant());
            var hub = _factory.CreateHubConnection(login.AccessToken);
            var name = role;
            hub.On<MenuChangedEvent>(HubEvents.MenuChanged, _ => changes.AddOrUpdate(name, 1, (_, n) => n + 1));
            await hub.StartAsync();
            await hub.InvokeAsync<DateTime>(HubMethods.Ping);
            screens.Add(hub);
            changes[name] = 0;
        }

        try
        {
            async Task AllSeeAsync(MenuEntryDto dish, int expected, string step)
            {
                foreach (var (role, client) in roles)
                {
                    var seen = (await Get<MenuDto>(client, "/api/menu")).Items.Single(i => i.Id == dish.Id).StockRemaining;
                    seen.Should().Be(expected, $"{step}: the {role} screen shows {dish.Name}");
                }

                var overview = await Get<StockOverviewDto>(roles["Manager"], "/api/menu-items/stock/overview");
                overview.Items.Single(i => i.MenuItemId == dish.Id).Available.Should().Be(expected, $"{step}: the stock dashboard agrees");
            }

            async Task EveryScreenHeardAsync(Dictionary<string, int> before, string step)
            {
                await WaitUntilAsync(() => roles.Keys.All(r => changes[r] > before[r]), () => $"{step}: " + string.Join(", ", changes.Select(c => c.Key + "=" + c.Value)));
            }

            Dictionary<string, int> Snapshot() => changes.ToDictionary(c => c.Key, c => c.Value);
            async Task SetAsync(MenuEntryDto dish, int quantity) =>
                (await kitchen.PatchAsJsonAsync($"/api/menu-items/{dish.Id}/stock", new SetStockRequest { Mode = StockTrackingMode.Exact, Quantity = quantity }, PosJson.Options)).EnsureSuccessStatusCode();
            async Task<TableDto> TableAsync(string code) =>
                (await Get<TableMapDto>(waiter1, "/api/tables")).Sections.SelectMany(s => s.Tables).Single(t => t.Code == code);
            async Task<HttpResponseMessage> OrderAsync(HttpClient client, string table, MenuEntryDto dish, int quantity, Guid? key = null)
            {
                var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
                {
                    Content = JsonContent.Create(new CreateOrderRequest
                    {
                        TableId = (await TableAsync(table)).Id, GuestCount = 2, Submit = true,
                        Items = new[] { new OrderItemInput { MenuItemId = dish.Id, Quantity = quantity } },
                    }, options: PosJson.Options),
                };
                request.Headers.Add(PosHeaders.IdempotencyKey, (key ?? Guid.NewGuid()).ToString());
                return await client.SendAsync(request);
            }

            // ---- initial counts, set by the kitchen: every screen shows them
            var before = Snapshot();
            await SetAsync(idli, 40);
            await SetAsync(biryani, 50);
            await SetAsync(grill, 4);
            await EveryScreenHeardAsync(before, "initial counts");
            await AllSeeAsync(idli, 40, "initial"); await AllSeeAsync(biryani, 50, "initial"); await AllSeeAsync(grill, 4, "initial");

            // ---- a waiter confirms 10 idlis: 30 everywhere
            before = Snapshot();
            var first = await Json<OrderDetailDto>(await OrderAsync(waiter1, "T01", idli, 10));
            await EveryScreenHeardAsync(before, "first order");
            await AllSeeAsync(idli, 30, "after the first order");

            // ---- the cashier bills the same order: the count does not move
            foreach (var ticket in first.Tickets)
            {
                (await kitchen.PostAsync($"/api/kitchen/orders/{ticket.Id}/start", null)).EnsureSuccessStatusCode();
                (await kitchen.PostAsync($"/api/kitchen/orders/{ticket.Id}/ready", null)).EnsureSuccessStatusCode();
            }

            await Json<OrderDetailDto>(await Send(waiter1, $"/api/orders/{first.Id}/serve", new { }));
            var bill = await Json<BillDetailDto>(await Send(waiter1, $"/api/orders/{first.Id}/request-bill", new { }));
            var counter = await _factory.CreateTerminalClientAsync("cashier1", ApiFactory.DemoPassword, "STOCK-COUNTER");
            (await Send(counter, $"/api/billing/{bill.Id}/payments", new AddPaymentRequest { PaymentMethodId = 1, Amount = bill.GrandTotal, RowVersion = bill.RowVersion })).StatusCode.Should().Be(HttpStatusCode.OK);
            await AllSeeAsync(idli, 30, "after the cashier billed and took payment");

            // ---- another waiter confirms 10 more: 20
            var second = await Json<OrderDetailDto>(await OrderAsync(waiter2, "T02", idli, 10));
            await AllSeeAsync(idli, 20, "after the second waiter's order");

            // ---- the kitchen refills 20: 40
            before = Snapshot();
            (await kitchen.PatchAsJsonAsync($"/api/menu-items/{idli.Id}/stock", new SetStockRequest { Mode = StockTrackingMode.Exact, Quantity = 20, Add = true }, PosJson.Options)).EnsureSuccessStatusCode();
            await EveryScreenHeardAsync(before, "refill");
            await AllSeeAsync(idli, 40, "after the refill");

            // ---- cancelling the second order gives its 10 back, once only
            (await Send(waiter2, $"/api/orders/{second.Id}/cancel", new CancelOrderRequest { Reason = "Guest left" })).StatusCode.Should().Be(HttpStatusCode.OK);
            await AllSeeAsync(idli, 50, "after the cancellation released its reservation");
            (await Send(waiter2, $"/api/orders/{second.Id}/cancel", new CancelOrderRequest { Reason = "Guest left" })).StatusCode.Should().NotBe(HttpStatusCode.OK);
            await AllSeeAsync(idli, 50, "a second cancel must not give the portions back again");

            // ---- the same order sent twice (a lost response, then a retry) is one order and one deduction
            var key = Guid.NewGuid();
            var a = await Json<OrderDetailDto>(await OrderAsync(waiter1, "T03", idli, 10, key));
            var b = await Json<OrderDetailDto>(await OrderAsync(waiter1, "T03", idli, 10, key));
            b.Id.Should().Be(a.Id, "the retry answers with the order that already exists");
            await AllSeeAsync(idli, 40, "the duplicate request deducted once");

            // ---- two waiters race for the last 4 grills: only one can have 3
            var race = await Task.WhenAll(OrderAsync(waiter1, "T04", grill, 3), OrderAsync(waiter2, "T05", grill, 3));
            race.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1, "4 grills cannot serve 3 + 3");
            var loser = race.Single(r => r.StatusCode != HttpStatusCode.Created);
            (await loser.Content.ReadAsStringAsync()).Should().Contain("Only 1 Grill left", "the second waiter is told what is left");
            await AllSeeAsync(grill, 1, "after the race");

            // ---- the last grill sells out everywhere
            (await OrderAsync(waiter1, "T06", grill, 1)).StatusCode.Should().Be(HttpStatusCode.Created);
            await AllSeeAsync(grill, 0, "after the last grill");
            foreach (var (role, client) in roles)
            {
                (await Get<MenuDto>(client, "/api/menu")).Items.Single(i => i.Id == grill.Id).IsAvailable.Should().BeFalse($"{role}: grill shows as sold out");
            }

            (await OrderAsync(waiter2, "T07", grill, 1)).StatusCode.Should().NotBe(HttpStatusCode.Created);
            await AllSeeAsync(biryani, 50, "biryani was never touched");

            // ---- every count is explained by its ledger: the movements add up to the count
            var history = await Get<StockHistoryDto>(kitchen, $"/api/menu-items/{idli.Id}/stock/history");
            history.Movements.Sum(m => m.QuantityChanged).Should().Be(40, "opening + orders + refill + release = what is on the count");
            history.Movements.Select(m => m.Type).Should().Equal(
                StockMovementType.OrderReservation, StockMovementType.OrderRelease, StockMovementType.Refill,
                StockMovementType.OrderReservation, StockMovementType.OrderReservation, StockMovementType.Opening);
        }
        finally
        {
            foreach (var screen in screens)
            {
                await screen.DisposeAsync();
            }
        }
    }

    private static async Task<T> Get<T>(HttpClient client, string url)
    {
        var envelope = await (await client.GetAsync(url)).ReadEnvelopeAsync<T>();
        envelope.Success.Should().BeTrue($"{url}: {envelope.Message}");
        return envelope.Data!;
    }

    private static async Task<T> Json<T>(HttpResponseMessage response)
    {
        var envelope = await response.ReadEnvelopeAsync<T>();
        envelope.Success.Should().BeTrue(envelope.Message);
        return envelope.Data!;
    }

    private static Task<HttpResponseMessage> Send(HttpClient client, string url, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body, body.GetType(), options: PosJson.Options) };
        request.Headers.Add(PosHeaders.IdempotencyKey, Guid.NewGuid().ToString());
        return client.SendAsync(request);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, Func<string> detail)
    {
        var until = DateTime.UtcNow.AddSeconds(15);
        while (!condition())
        {
            if (DateTime.UtcNow > until)
            {
                throw new TimeoutException("The live update did not arrive in 15 seconds. " + detail());
            }

            await Task.Delay(50);
        }
    }
}
