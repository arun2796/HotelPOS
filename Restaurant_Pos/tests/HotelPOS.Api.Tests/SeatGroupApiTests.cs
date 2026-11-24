using System.Net;
using System.Net.Http.Json;
using HotelPOS.Api.Common;
using HotelPOS.Api.Tests.Support;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Kitchen;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Orders;

namespace HotelPOS.Api.Tests;

// Seat groups of one table: a seat belongs to one group, each group has tickets and a kitchen status of its own, bills can be
// made per group, and a table's seats cannot be given back while the order still uses them. Every test makes its own tables.
public class SeatGroupApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public SeatGroupApiTests(ApiFactory factory, Xunit.Abstractions.ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    [Fact]
    public async Task ASeatCannotBelongToTwoSeatGroups_OfTheSameOrder()
    {
        var waiter = await _factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword);
        var table = await NewTableAsync("SG1", 4);
        var item = await ItemAsync(waiter);

        var order = await PostJson<OrderDetailDto>(waiter, "/api/orders", new CreateOrderRequest
        {
            TableId = table.Id, GuestCount = 4, Submit = true,
            Items = new[]
            {
                new OrderItemInput { MenuItemId = item.Id, SeatCode = "SG1A+B" },
                new OrderItemInput { MenuItemId = item.Id, SeatCode = "SG1C" },
            },
        });

        // seat B is already in the group A+B: it cannot also start its own group, nor join C's
        (await Post(waiter, $"/api/orders/{order.Id}/items", new AppendOrderItemsRequest
        {
            Items = new[] { new OrderItemInput { MenuItemId = item.Id, SeatCode = "SG1B" } },
        })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Post(waiter, $"/api/orders/{order.Id}/items", new AppendOrderItemsRequest
        {
            Items = new[] { new OrderItemInput { MenuItemId = item.Id, SeatCode = "SG1B+C" } },
        })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // adding to an existing group, or to a free seat, is fine
        (await Post(waiter, $"/api/orders/{order.Id}/items", new AppendOrderItemsRequest
        {
            Items = new[] { new OrderItemInput { MenuItemId = item.Id, SeatCode = "SG1A+B" }, new OrderItemInput { MenuItemId = item.Id, SeatCode = "SG1D" } },
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = (await (await waiter.GetAsync($"/api/orders/{order.Id}")).ReadEnvelopeAsync<OrderDetailDto>()).Data!;
        detail.SeatGroups.Select(g => g.SeatCode).Should().Equal("SG1A+B", "SG1C", "SG1D");
    }

    [Fact]
    public async Task EachSeatGroupHasItsOwnTicket_AndItsOwnKitchenStatus()
    {
        var waiter = await _factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword);
        var kitchen = await _factory.CreateAuthenticatedClientAsync("kitchen1", ApiFactory.DemoPassword);
        var table = await NewTableAsync("SG2", 4);
        var item = await ItemAsync(waiter);

        var order = await PostJson<OrderDetailDto>(waiter, "/api/orders", new CreateOrderRequest
        {
            TableId = table.Id, GuestCount = 3, Submit = true,
            Items = new[]
            {
                new OrderItemInput { MenuItemId = item.Id, Quantity = 2, SeatCode = "SG2A+B" },
                new OrderItemInput { MenuItemId = item.Id, SeatCode = "SG2C" },
            },
        });

        order.Tickets.Should().HaveCount(2, "a ticket never mixes seat groups");
        order.Tickets.Select(t => t.SeatCode).Should().BeEquivalentTo("SG2A+B", "SG2C");

        // the kitchen finishes only the A+B ticket
        var first = order.Tickets.Single(t => t.SeatCode == "SG2A+B");
        (await kitchen.PostAsync($"/api/kitchen/orders/{first.Id}/start", null)).EnsureSuccessStatusCode();
        (await kitchen.PostAsync($"/api/kitchen/orders/{first.Id}/ready", null)).EnsureSuccessStatusCode();

        var detail = (await (await waiter.GetAsync($"/api/orders/{order.Id}")).ReadEnvelopeAsync<OrderDetailDto>()).Data!;
        detail.SeatGroups.Single(g => g.SeatCode == "SG2A+B").KitchenStatus.Should().Be(KitchenOrderStatus.Ready);
        detail.SeatGroups.Single(g => g.SeatCode == "SG2C").KitchenStatus.Should().Be(KitchenOrderStatus.New, "seat C does not inherit A+B's status");

        var tickets = (await (await kitchen.GetAsync("/api/kitchen/orders")).ReadEnvelopeAsync<KitchenTicketListDto>()).Data!.Tickets;
        tickets.Where(t => t.OrderId == order.Id).Select(t => t.SeatCode).Should().BeEquivalentTo("SG2A+B", "SG2C");
    }

    [Fact]
    public async Task RequestingTheBillBySeatGroup_MakesOneBillPerGroup_WithEveryDishOnceAndTheTableStaysTaken()
    {
        var waiter = await _factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword);
        var kitchen = await _factory.CreateAuthenticatedClientAsync("kitchen1", ApiFactory.DemoPassword);
        var table = await NewTableAsync("SG3", 4);
        var item = await ItemAsync(waiter);

        var order = await PostJson<OrderDetailDto>(waiter, "/api/orders", new CreateOrderRequest
        {
            TableId = table.Id, GuestCount = 4, Submit = true,
            Items = new[]
            {
                new OrderItemInput { MenuItemId = item.Id, Quantity = 2, SeatCode = "SG3A+B" },
                new OrderItemInput { MenuItemId = item.Id, SeatCode = "SG3C" },
                new OrderItemInput { MenuItemId = item.Id, SeatCode = "SG3D" },
            },
        });
        foreach (var ticket in order.Tickets)
        {
            (await kitchen.PostAsync($"/api/kitchen/orders/{ticket.Id}/start", null)).EnsureSuccessStatusCode();
            (await kitchen.PostAsync($"/api/kitchen/orders/{ticket.Id}/ready", null)).EnsureSuccessStatusCode();
        }

        await PostJson<OrderDetailDto>(waiter, $"/api/orders/{order.Id}/serve", new { });
        var bill = await PostJson<BillDetailDto>(waiter, $"/api/orders/{order.Id}/request-bill?splitBySeat=true", new { });

        var manager = await _factory.CreateTerminalClientAsync("manager1", ApiFactory.DemoPassword, "SEATGROUP-MGR");
        var pending = (await (await manager.GetAsync("/api/billing/pending")).ReadEnvelopeAsync<List<BillSummaryDto>>()).Data!.Where(b => b.OrderId == order.Id).ToList();
        pending.Should().HaveCount(3, "one bill for A+B, one for C and one for D");
        var details = new List<BillDetailDto>();
        foreach (var part in pending)
        {
            details.Add((await (await manager.GetAsync($"/api/billing/{part.Id}")).ReadEnvelopeAsync<BillDetailDto>()).Data!);
        }

        details.SelectMany(d => d.Items).Sum(i => i.Quantity).Should().Be(4, "every portion is on exactly one bill");
        details.SelectMany(d => d.Items).Select(i => i.SeatCode).Should().BeEquivalentTo("SG3A+B", "SG3C", "SG3D");
        details.Should().OnlyContain(d => d.PaymentStatus == PaymentStatus.Pending && d.Status == BillStatus.Open);
        details.Sum(d => d.Subtotal).Should().Be(order.Items.Sum(i => i.LineTotal), "nothing is lost or counted twice");

        // the table is not freed while any of its bills is unpaid
        (await NewTableLookupAsync(waiter, "SG3")).Status.Should().NotBe(TableStatus.Available);
    }

    [Fact]
    public async Task SeatsOfAMergedTable_CannotBeGivenBackWhileTheOrderUsesThem()
    {
        var waiter = await _factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword);
        var primary = await NewTableAsync("SG4", 4);
        var companion = await NewTableAsync("SG5", 4);
        var item = await ItemAsync(waiter);

        await PostJson<TableDto>(waiter, $"/api/tables/{primary.Id}/occupy", new OccupyTableRequest { GuestCount = 2, RowVersion = primary.RowVersion });
        (await Post(waiter, $"/api/tables/{primary.Id}/merge", new MergeTablesRequest { CompanionTableId = companion.Id })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostJson<OrderDetailDto>(waiter, "/api/orders", new CreateOrderRequest
        {
            TableId = primary.Id, GuestCount = 6, Submit = true,
            Items = new[] { new OrderItemInput { MenuItemId = item.Id, SeatCode = "SG5A" } },
        })).Id.Should().BeGreaterThan(0);

        (await Post(waiter, $"/api/tables/{companion.Id}/unmerge", new { })).StatusCode.Should().NotBe(HttpStatusCode.OK, "seat SG5A is used by the order");
        (await NewTableLookupAsync(waiter, "SG5")).MergedIntoTableId.Should().Be(primary.Id, "the table is still merged");
    }

    [Fact]
    public async Task TwoWaitersAppendingOverlappingSeatGroupsAtTheSameTime_OnlyOneSucceeds_AndTheLoserCanRetrySafely()
    {
        var waiter = await _factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword);
        var manager = await _factory.CreateAuthenticatedClientAsync("manager1", ApiFactory.DemoPassword);
        var item = await ItemAsync(waiter);

        for (var round = 1; round <= 5; round++)
        {
            var code = "SGR" + round;
            var table = await NewTableAsync(code, 4);
            var order = await PostJson<OrderDetailDto>(waiter, "/api/orders", new CreateOrderRequest
            {
                TableId = table.Id, GuestCount = 4, Submit = true,
                Items = new[] { new OrderItemInput { MenuItemId = item.Id, SeatCode = code + "D" } },
            });

            // A+B and B+C overlap on seat B: two terminals send them at the same moment
            var first = Post(waiter, $"/api/orders/{order.Id}/items", new AppendOrderItemsRequest
            {
                Items = new[] { new OrderItemInput { MenuItemId = item.Id, SeatCode = code + "A+B" } },
            });
            var second = Post(manager, $"/api/orders/{order.Id}/items", new AppendOrderItemsRequest
            {
                Items = new[] { new OrderItemInput { MenuItemId = item.Id, SeatCode = code + "B+C" } },
            });
            var results = await Task.WhenAll(first, second);
            _output.WriteLine($"round {round}: first={(int)results[0].StatusCode} second={(int)results[1].StatusCode}");

            results.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1, $"round {round}: only one of two overlapping groups may be saved");
            var loser = results.Single(r => r.StatusCode != HttpStatusCode.OK);
            loser.StatusCode.Should().BeOneOf(HttpStatusCode.Conflict, HttpStatusCode.BadRequest);
            (await loser.Content.ReadAsStringAsync()).Should().NotBeNullOrWhiteSpace("the losing terminal is told why");

            var detail = (await (await waiter.GetAsync($"/api/orders/{order.Id}")).ReadEnvelopeAsync<OrderDetailDto>()).Data!;
            detail.Items.Count(i => i.Status != OrderItemStatus.Cancelled).Should().Be(2, "the first dish plus exactly one new line");
            var groups = detail.SeatGroups.Select(g => g.SeatCode).ToList();
            groups.Should().HaveCount(2);
            groups.Should().Contain(code + "D");
            (groups.Contains(code + "A+B") ^ groups.Contains(code + "B+C")).Should().BeTrue("exactly one of the overlapping groups exists, never both");
            detail.Tickets.Should().HaveCount(2, "one ticket for D and one for the winner: no duplicate tickets");
            detail.Tickets.Select(t => t.TicketNumber).Should().OnlyHaveUniqueItems();

            // the loser refreshes and retries: it is now refused with a message that names the seat (no silent duplicate)
            var losingSeat = groups.Contains(code + "A+B") ? code + "B+C" : code + "A+B";
            var retry = await Post(manager, $"/api/orders/{order.Id}/items", new AppendOrderItemsRequest
            {
                Items = new[] { new OrderItemInput { MenuItemId = item.Id, SeatCode = losingSeat } },
            });
            retry.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await retry.Content.ReadAsStringAsync()).Should().Contain("already belongs");
        }
    }

    private async Task<TableDto> NewTableAsync(string code, int capacity)
    {
        var manager = await _factory.CreateAuthenticatedClientAsync("manager1", ApiFactory.DemoPassword);
        var section = (await (await manager.GetAsync("/api/sections")).ReadEnvelopeAsync<List<SectionDto>>()).Data!.First(s => s.IsActive);
        var response = await manager.PostAsJsonAsync("/api/tables", new CreateTableRequest { Code = code, SectionId = section.Id, Capacity = capacity }, PosJson.Options);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await NewTableLookupAsync(manager, code));
    }

    private static async Task<TableDto> NewTableLookupAsync(HttpClient client, string code) =>
        (await (await client.GetAsync("/api/tables")).ReadEnvelopeAsync<TableMapDto>()).Data!.Sections.SelectMany(s => s.Tables).Single(t => t.Code == code);

    private static async Task<MenuEntryDto> ItemAsync(HttpClient client) =>
        (await (await client.GetAsync("/api/menu")).ReadEnvelopeAsync<MenuDto>()).Data!.Items
        .First(i => i.IsAvailable && i.ModifierGroupIds.Count == 0 && i.Price > 0 && i.StockMode == StockTrackingMode.None && i.Name != "Butter Naan");

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
