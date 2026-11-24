using System.Net;
using System.Net.Http.Json;
using HotelPOS.Api.Common;
using HotelPOS.Api.Tests.Support;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Kitchen;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Orders;
using HotelPOS.Contracts.Reservations;

namespace HotelPOS.Api.Tests;

// Regression tests for the defects the live role-by-role test run found (2026-11-24).
public class LiveFindingsApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public LiveFindingsApiTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Recall_OnlyWorksOnAReadyTicket()
    {
        var (order, _) = await SubmittedOrderAsync("T01", ("T01A", 1));
        var kitchen = await _factory.CreateAuthenticatedClientAsync("kitchen1", ApiFactory.DemoPassword);

        var recall = await kitchen.PostAsync($"/api/kitchen/orders/{order.Tickets[0].Id}/recall", null);

        recall.StatusCode.Should().Be(HttpStatusCode.Conflict, "a New ticket has nothing to recall");
    }

    [Fact]
    public async Task CancellingAnItemThatIsBeingPrepared_NeedsAReason()
    {
        var (order, _) = await SubmittedOrderAsync("T02", ("T02A", 1), ("T02B", 1));
        var kitchen = await _factory.CreateAuthenticatedClientAsync("kitchen1", ApiFactory.DemoPassword);
        (await kitchen.PostAsync($"/api/kitchen/orders/{order.Tickets[0].Id}/start", null)).EnsureSuccessStatusCode();
        var manager = await _factory.CreateAuthenticatedClientAsync("manager1", ApiFactory.DemoPassword);
        var line = order.Items.First();

        var noReason = await Post(manager, $"/api/orders/{order.Id}/items/{line.Id}/cancel", new CancelOrderItemRequest());
        var withReason = await Post(manager, $"/api/orders/{order.Id}/items/{line.Id}/cancel", new CancelOrderItemRequest { Reason = "Guest left" });

        noReason.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        withReason.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Settings_RejectValuesThatAreOfTheRightTypeButWrong()
    {
        var admin = await _factory.CreateAuthenticatedClientAsync("admin", ApiFactory.AdminPassword);
        foreach (var (key, value) in new[]
        {
            (SettingKeys.Gstin, "NOTAGSTIN"), (SettingKeys.RestaurantName, ""), (SettingKeys.KitchenWarnMinutes, "-5"),
            (SettingKeys.LockoutThreshold, "0"), (SettingKeys.TaxSplitDisplay, "Bogus"), (SettingKeys.InvoiceResetPolicy, "Bogus"),
        })
        {
            var response = await admin.PutAsJsonAsync("/api/admin/settings",
                new UpdateSettingsRequest { Items = new[] { new SettingValue { Key = key, Value = value } } }, PosJson.Options);
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"{key} = '{value}' must be refused");
        }
    }

    [Fact]
    public async Task AFixedDiscountLargerThanTheBill_AndACustomerEditAfterTheInvoice_AreRefused()
    {
        var (order, _) = await SubmittedOrderAsync("T03", ("T03A", 1));
        var kitchen = await _factory.CreateAuthenticatedClientAsync("kitchen1", ApiFactory.DemoPassword);
        foreach (var ticket in order.Tickets)
        {
            (await kitchen.PostAsync($"/api/kitchen/orders/{ticket.Id}/start", null)).EnsureSuccessStatusCode();
            (await kitchen.PostAsync($"/api/kitchen/orders/{ticket.Id}/ready", null)).EnsureSuccessStatusCode();
        }

        var waiter = await _factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword);
        await PostJson<OrderDetailDto>(waiter, $"/api/orders/{order.Id}/serve", new { });
        var bill = await PostJson<BillDetailDto>(waiter, $"/api/orders/{order.Id}/request-bill", new { });
        var manager = await _factory.CreateTerminalClientAsync("manager1", ApiFactory.DemoPassword, "LIVEFIND-MGR");
        (await manager.PostAsync($"/api/billing/{bill.Id}/claim", null)).EnsureSuccessStatusCode();

        var tooBig = await manager.PutAsJsonAsync($"/api/billing/{bill.Id}/discount", new ApplyDiscountRequest
        {
            Type = DiscountType.FixedAmount, Value = 99999m, Reason = "test", RowVersion = (await GetBill(manager, bill.Id)).RowVersion,
        }, PosJson.Options);
        tooBig.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var finalized = await PostJson<BillDetailDto>(manager, $"/api/billing/{bill.Id}/finalize", new BillActionRequest { RowVersion = (await GetBill(manager, bill.Id)).RowVersion });
        var late = await manager.PutAsJsonAsync($"/api/billing/{bill.Id}/customer",
            new UpdateCustomerRequest { Name = "Late edit", RowVersion = finalized.RowVersion }, PosJson.Options);
        late.StatusCode.Should().Be(HttpStatusCode.Conflict, "the invoice number is already issued");
    }

    [Fact]
    public async Task Reservations_RejectAPastStartTime_AndAPhoneThatIsNotANumber()
    {
        var waiter = await _factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword);
        var table = await TableAsync(waiter, "T04");
        var manager = await _factory.CreateAuthenticatedClientAsync("manager1", ApiFactory.DemoPassword);

        var past = DateTime.UtcNow.AddHours(-6);
        var inThePast = await manager.PostAsJsonAsync("/api/reservations", new CreateReservationRequest
        {
            TableId = table.Id, CustomerName = "Past Guest", CustomerPhone = "9876543210", PartySize = 2, StartTimeUtc = past, EndTimeUtc = past.AddHours(1),
        }, PosJson.Options);
        var future = DateTime.UtcNow.AddDays(1);
        var badPhone = await manager.PostAsJsonAsync("/api/reservations", new CreateReservationRequest
        {
            TableId = table.Id, CustomerName = "Phone Guest", CustomerPhone = "abc", PartySize = 2, StartTimeUtc = future, EndTimeUtc = future.AddHours(1),
        }, PosJson.Options);

        inThePast.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        badPhone.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SeatingAReservation_OccupiesTheTableInTheSameStep()
    {
        var manager = await _factory.CreateAuthenticatedClientAsync("manager1", ApiFactory.DemoPassword);
        var table = await TableAsync(manager, "T06");
        var start = DateTime.UtcNow.AddHours(2);
        var created = await PostJson<ReservationDto>(manager, "/api/reservations", new CreateReservationRequest
        {
            TableId = table.Id, CustomerName = "Seat Guest", CustomerPhone = "9876501234", PartySize = 3, StartTimeUtc = start, EndTimeUtc = start.AddHours(1),
        });

        var seated = await PostJson<ReservationDto>(manager, $"/api/reservations/{created.Id}/seat", new SeatReservationRequest { RowVersion = created.RowVersion });

        seated.Status.Should().Be(ReservationStatus.Seated);
        var after = await TableAsync(manager, "T06");
        after.Status.Should().Be(TableStatus.Occupied, "nobody else may take the table between seating and opening it");
        after.GuestCount.Should().Be(3);
    }

    [Fact]
    public async Task TableDetails_CarryTheRunningTotalOfTheOpenOrder()
    {
        var (order, table) = await SubmittedOrderAsync("T05", ("T05A", 2));
        var waiter = await _factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword);

        var detail = (await (await waiter.GetAsync($"/api/tables/{table.Id}")).ReadEnvelopeAsync<TableDetailDto>()).Data!;

        detail.Table.CurrentOrderTotal.Should().Be(order.Items.Sum(i => i.LineTotal));
    }

    [Fact]
    public async Task VoidingABill_PutsTheFoodBackOnTheCount_OrWritesItOff_AsTheCashierChooses()
    {
        var kitchen = await _factory.CreateAuthenticatedClientAsync("kitchen1", ApiFactory.DemoPassword);
        var waiter = await _factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword);
        var manager = await _factory.CreateTerminalClientAsync("manager1", ApiFactory.DemoPassword, "LIVEFIND-MGR");
        var menu = (await (await waiter.GetAsync("/api/menu")).ReadEnvelopeAsync<MenuDto>()).Data!;
        var item = menu.Items.Last(i => i.IsAvailable && i.ModifierGroupIds.Count == 0 && i.Price > 0 && i.Name != "Butter Naan");

        async Task<(int? AfterOrder, int? AfterVoid, IReadOnlyList<StockMovementDto> Ledger)> RunAsync(string tableCode, StockDisposition choice)
        {
            (await kitchen.PatchAsJsonAsync($"/api/menu-items/{item.Id}/stock",
                new SetStockRequest { Mode = StockTrackingMode.Exact, Quantity = 20 }, PosJson.Options)).EnsureSuccessStatusCode();
            var table = await TableAsync(waiter, tableCode);
            var order = await PostJson<OrderDetailDto>(waiter, "/api/orders", new CreateOrderRequest
            {
                TableId = table.Id, GuestCount = 1, Submit = true,
                Items = new[] { new OrderItemInput { MenuItemId = item.Id, Quantity = 4 } },
            });
            var afterOrder = await StockAsync(waiter, item.Id);
            foreach (var ticket in order.Tickets)
            {
                (await kitchen.PostAsync($"/api/kitchen/orders/{ticket.Id}/start", null)).EnsureSuccessStatusCode();
                (await kitchen.PostAsync($"/api/kitchen/orders/{ticket.Id}/ready", null)).EnsureSuccessStatusCode();
            }

            await PostJson<OrderDetailDto>(waiter, $"/api/orders/{order.Id}/serve", new { });
            var bill = await PostJson<BillDetailDto>(waiter, $"/api/orders/{order.Id}/request-bill", new { });
            (await manager.PostAsync($"/api/billing/{bill.Id}/claim", null)).EnsureSuccessStatusCode();
            var finalized = await PostJson<BillDetailDto>(manager, $"/api/billing/{bill.Id}/finalize", new BillActionRequest { RowVersion = (await GetBill(manager, bill.Id)).RowVersion });
            var voided = await Post(manager, $"/api/billing/{bill.Id}/void", new VoidBillRequest
            {
                Reason = "Guest left", FoodDisposition = choice, RowVersion = finalized.RowVersion,
            });
            voided.StatusCode.Should().Be(HttpStatusCode.OK);

            var history = (await (await kitchen.GetAsync($"/api/menu-items/{item.Id}/stock/history")).ReadEnvelopeAsync<StockHistoryDto>()).Data!;
            return (afterOrder, await StockAsync(waiter, item.Id), history.Movements.Where(m => m.OrderId == order.Id).ToList());
        }

        var restocked = await RunAsync("O03", StockDisposition.Restock);
        restocked.AfterOrder.Should().Be(16);
        restocked.AfterVoid.Should().Be(20, "returned food goes back on the count");
        restocked.Ledger.Select(m => m.Type).Should().Contain(StockMovementType.OrderRelease).And.NotContain(StockMovementType.Waste);

        var wasted = await RunAsync("O04", StockDisposition.Waste);
        wasted.AfterOrder.Should().Be(16);
        wasted.AfterVoid.Should().Be(16, "written-off food stays off the count");
        wasted.Ledger.Where(m => m.Type == StockMovementType.Waste).Should().ContainSingle().Which.QuantityChanged.Should().Be(-4);
    }

    [Fact]
    public async Task ACashier_CanTakeAnOrder_AndTheStockStatusFollowsTheLowLevel()
    {
        var cashier = await _factory.CreateAuthenticatedClientAsync("cashier1", ApiFactory.DemoPassword);
        var kitchen = await _factory.CreateAuthenticatedClientAsync("kitchen1", ApiFactory.DemoPassword);
        var menu = (await (await cashier.GetAsync("/api/menu")).ReadEnvelopeAsync<MenuDto>()).Data!;
        var item = menu.Items.First(i => i.IsAvailable && i.ModifierGroupIds.Count == 0 && i.Price > 0 && i.Name != "Butter Naan");

        (await kitchen.PatchAsJsonAsync($"/api/menu-items/{item.Id}/stock",
            new SetStockRequest { Mode = StockTrackingMode.Exact, Quantity = 12 }, PosJson.Options)).EnsureSuccessStatusCode();
        (await kitchen.PatchAsJsonAsync($"/api/menu-items/{item.Id}/stock/reorder-level",
            new SetReorderLevelRequest { ReorderLevel = 10 }, PosJson.Options)).EnsureSuccessStatusCode();

        // the cashier takes a counter order: it succeeds and the shared count goes down
        var order = await PostJson<OrderDetailDto>(cashier, "/api/orders", new CreateOrderRequest
        {
            OrderType = OrderType.Takeaway, GuestCount = 1, Submit = true,
            Items = new[] { new OrderItemInput { MenuItemId = item.Id, Quantity = 3 } },
        });
        order.Id.Should().BeGreaterThan(0);

        var overview = (await (await kitchen.GetAsync("/api/menu-items/stock/overview")).ReadEnvelopeAsync<StockOverviewDto>()).Data!;
        var row = overview.Items.Single(r => r.MenuItemId == item.Id);
        row.Available.Should().Be(9);
        row.Reserved.Should().Be(3, "the dishes are with the kitchen and not served yet");
        row.Sold.Should().Be(3);
        row.ReorderLevel.Should().Be(10);
        row.Status.Should().Be(StockStatus.Low, "9 is at or below the low-stock level of 10");
        row.UpdatedBy.Should().NotBeNullOrEmpty();

        // a waiter may not change the count
        (await Post(await _factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword), $"/api/menu-items/{item.Id}/stock/adjust",
            new AdjustStockRequest { Kind = StockMovementType.Adjustment, Quantity = -1, Reason = "x" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static async Task<int?> StockAsync(HttpClient client, int itemId) =>
        (await (await client.GetAsync("/api/menu")).ReadEnvelopeAsync<MenuDto>()).Data!.Items.Single(i => i.Id == itemId).StockRemaining;

    private async Task<(OrderDetailDto Order, TableDto Table)> SubmittedOrderAsync(string tableCode, params (string Seat, int Quantity)[] lines)
    {
        var waiter = await _factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword);
        var table = await TableAsync(waiter, tableCode);
        var menu = (await (await waiter.GetAsync("/api/menu")).ReadEnvelopeAsync<MenuDto>()).Data!;
        var item = menu.Items.First(i => i.IsAvailable && i.ModifierGroupIds.Count == 0 && i.Price > 0);
        var order = await PostJson<OrderDetailDto>(waiter, "/api/orders", new CreateOrderRequest
        {
            TableId = table.Id, GuestCount = lines.Length, Submit = true,
            Items = lines.Select(l => new OrderItemInput { MenuItemId = item.Id, Quantity = l.Quantity, SeatCode = l.Seat }).ToList(),
        });
        return (order, table);
    }

    private static async Task<TableDto> TableAsync(HttpClient client, string code) =>
        (await (await client.GetAsync("/api/tables")).ReadEnvelopeAsync<TableMapDto>()).Data!.Sections.SelectMany(s => s.Tables).Single(t => t.Code == code);

    private static async Task<BillDetailDto> GetBill(HttpClient client, int id) =>
        (await (await client.GetAsync($"/api/billing/{id}")).ReadEnvelopeAsync<BillDetailDto>()).Data!;

    private static Task<HttpResponseMessage> Post(HttpClient client, string url, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body, body.GetType(), options: PosJson.Options) };
        request.Headers.Add(PosHeaders.IdempotencyKey, Guid.NewGuid().ToString());
        return client.SendAsync(request);
    }

    private static async Task<T> PostJson<T>(HttpClient client, string url, object body)
    {
        var envelope = await (await Post(client, url, body)).ReadEnvelopeAsync<T>();
        envelope.Success.Should().BeTrue(envelope.Message);
        return envelope.Data!;
    }
}
