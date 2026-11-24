using System.Net;
using System.Net.Http.Json;
using HotelPOS.Api.Common;
using HotelPOS.Api.Tests.Support;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Orders;

namespace HotelPOS.Api.Tests;

// One table, several guests, separate bills: a table of four seats pays as seat 1A, seat 1B and seats 1C+1D.
public class SplitBillApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public SplitBillApiTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SplitBySeat_GivesEveryPartItsOwnInvoice_AndFreesTheTableOnlyWhenTheLastPartIsPaid()
    {
        var (order, table) = await ServedOrderAsync("T01", ("T01A", 2), ("T01B", 1), ("T01C", 1), (null, 1));
        var bill = await RequestBillAsync(order.Id);
        bill.Items.Select(i => i.SeatCode).Should().BeEquivalentTo("T01A", "T01B", "T01C", null);
        var cashier = await _factory.CreateTerminalClientAsync("cashier1", ApiFactory.DemoPassword, "BILLING-SPLIT");
        await ClaimAsync(cashier, bill);

        // Seat A stays; seat B and seats C + whole-table go to their own bills.
        var seatB = bill.Items.Single(i => i.SeatCode == "T01B").Id;
        var seatC = bill.Items.Single(i => i.SeatCode == "T01C").Id;
        var whole = bill.Items.Single(i => i.SeatCode == null).Id;
        var split = await PostAsync<BillDetailDto>(cashier, $"/api/billing/{bill.Id}/split", new SplitBillRequest
        {
            RowVersion = (await GetBillAsync(cashier, bill.Id)).RowVersion,
            Parts = new[] { new SplitBillPart { BillItemIds = new[] { seatB } }, new SplitBillPart { BillItemIds = new[] { seatC, whole } } },
        });

        split.SiblingBillIds.Should().HaveCount(3);
        split.TableCode.Should().Be("T01 (A)");
        split.Items.Should().ContainSingle(i => i.SeatCode == "T01A");
        var parts = new List<BillDetailDto>();
        foreach (var id in split.SiblingBillIds)
        {
            parts.Add(await GetBillAsync(cashier, id));
        }

        parts.Select(p => p.TableCode).Should().Equal("T01 (A)", "T01 (B)", "T01 (C)");
        parts.Sum(p => p.Items.Sum(i => i.Quantity)).Should().Be(5, "no line is lost or doubled by the split");
        parts.Sum(p => p.Subtotal).Should().Be(bill.Subtotal);

        // Pay A and B: the table stays billing. Pay C: the table is free.
        var invoices = new List<string>();
        foreach (var part in parts.Take(2))
        {
            await ClaimAsync(cashier, part);
            var paid = await PayAsync(cashier, await GetBillAsync(cashier, part.Id), part.GrandTotal);
            paid.Status.Should().Be(BillStatus.Settled);
            invoices.Add(paid.InvoiceNumber!);
            (await TableAsync(cashier, "T01")).Status.Should().Be(TableStatus.Billing, "another part of the table's bill is still unpaid");
            (await GetOrderAsync(cashier, order.Id)).Status.Should().Be(OrderStatus.Billed);
        }

        await ClaimAsync(cashier, parts[2]);
        var last = await PayAsync(cashier, await GetBillAsync(cashier, parts[2].Id), parts[2].GrandTotal);
        invoices.Add(last.InvoiceNumber!);

        invoices.Distinct().Should().HaveCount(3, "every part is its own invoice");
        (await TableAsync(cashier, "T01")).Status.Should().Be(TableStatus.Available);
        (await GetOrderAsync(cashier, order.Id)).Status.Should().BeOneOf(OrderStatus.Paid, OrderStatus.Completed);
    }

    [Fact]
    public async Task FixedDiscount_IsSharedBetweenTheParts_AndAddsUpToTheOriginal()
    {
        var (order, _) = await ServedOrderAsync("T02", ("T02A", 1), ("T02B", 3));
        var bill = await RequestBillAsync(order.Id);
        var manager = await _factory.CreateTerminalClientAsync("manager1", ApiFactory.DemoPassword, "BILLING-SPLIT-2");
        await ClaimAsync(manager, bill);
        var discounted = await PutAsync<BillDetailDto>(manager, $"/api/billing/{bill.Id}/discount", new ApplyDiscountRequest
        {
            Type = DiscountType.FixedAmount, Value = 100m, Reason = "Regular guest", RowVersion = (await GetBillAsync(manager, bill.Id)).RowVersion,
        });

        var split = await PostAsync<BillDetailDto>(manager, $"/api/billing/{bill.Id}/split", new SplitBillRequest
        {
            RowVersion = discounted.RowVersion,
            Parts = new[] { new SplitBillPart { BillItemIds = new[] { discounted.Items.Single(i => i.SeatCode == "T02B").Id } } },
        });

        var other = await GetBillAsync(manager, split.SiblingBillIds.Single(id => id != bill.Id));
        (split.DiscountAmount + other.DiscountAmount).Should().Be(100m);
        (split.Subtotal + other.Subtotal).Should().Be(bill.Subtotal);
    }

    [Fact]
    public async Task Reopen_UndoesTheWholeSplit_AndAFinalizedOrPaidSplitCannotBeReopened()
    {
        var (order, _) = await ServedOrderAsync("T03", ("T03A", 1), ("T03B", 1));
        var bill = await RequestBillAsync(order.Id);
        var cashier = await _factory.CreateTerminalClientAsync("cashier1", ApiFactory.DemoPassword, "BILLING-SPLIT");
        await ClaimAsync(cashier, bill);
        var split = await PostAsync<BillDetailDto>(cashier, $"/api/billing/{bill.Id}/split", new SplitBillRequest
        {
            RowVersion = (await GetBillAsync(cashier, bill.Id)).RowVersion,
            Parts = new[] { new SplitBillPart { BillItemIds = new[] { bill.Items.Single(i => i.SeatCode == "T03B").Id } } },
        });

        var reopened = await cashier.PostAsJsonAsync($"/api/billing/{bill.Id}/reopen",
            new ReopenBillRequest { Reason = "Wrong split", RowVersion = split.RowVersion }, PosJson.Options);
        reopened.StatusCode.Should().Be(HttpStatusCode.OK);

        var pending = (await (await cashier.GetAsync("/api/billing/pending")).ReadEnvelopeAsync<List<BillSummaryDto>>()).Data!;
        pending.Where(b => b.OrderId == order.Id).Should().BeEmpty("both parts are void after the split is undone");
        (await GetOrderAsync(cashier, order.Id)).Status.Should().Be(OrderStatus.Served);
    }

    [Fact]
    public async Task Split_IsRefused_WhenItWouldEmptyTheBill_OrWhenTheBillIsAlreadyFinalized()
    {
        var (order, _) = await ServedOrderAsync("T04", ("T04A", 1), ("T04B", 1));
        var bill = await RequestBillAsync(order.Id);
        var cashier = await _factory.CreateTerminalClientAsync("cashier1", ApiFactory.DemoPassword, "BILLING-SPLIT");
        await ClaimAsync(cashier, bill);

        var all = await SendAsync(cashier, $"/api/billing/{bill.Id}/split", new SplitBillRequest
        {
            RowVersion = (await GetBillAsync(cashier, bill.Id)).RowVersion,
            Parts = new[] { new SplitBillPart { BillItemIds = bill.Items.Select(i => i.Id).ToList() } },
        });
        all.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var finalized = await PostAsync<BillDetailDto>(cashier, $"/api/billing/{bill.Id}/finalize",
            new BillActionRequest { RowVersion = (await GetBillAsync(cashier, bill.Id)).RowVersion });
        var late = await SendAsync(cashier, $"/api/billing/{bill.Id}/split", new SplitBillRequest
        {
            RowVersion = finalized.RowVersion,
            Parts = new[] { new SplitBillPart { BillItemIds = new[] { bill.Items.First().Id } } },
        });
        late.StatusCode.Should().NotBe(HttpStatusCode.OK, "a finalized bill already has its invoice number");
    }

    private async Task<(OrderDetailDto Order, TableDto Table)> ServedOrderAsync(string tableCode, params (string? Seat, int Quantity)[] lines)
    {
        var waiter = await _factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword);
        var table = await TableAsync(waiter, tableCode);
        var menu = (await (await waiter.GetAsync("/api/menu")).ReadEnvelopeAsync<MenuDto>()).Data!;
        var item = menu.Items.First(i => i.IsAvailable && i.ModifierGroupIds.Count == 0 && i.Price > 0);
        var order = await PostAsync<OrderDetailDto>(waiter, "/api/orders", new CreateOrderRequest
        {
            TableId = table.Id, GuestCount = lines.Length, Submit = true,
            Items = lines.Select(l => new OrderItemInput { MenuItemId = item.Id, Quantity = l.Quantity, SeatCode = l.Seat }).ToList(),
        });
        var kitchen = await _factory.CreateAuthenticatedClientAsync("kitchen1", ApiFactory.DemoPassword);
        foreach (var ticket in order.Tickets)
        {
            (await kitchen.PostAsync($"/api/kitchen/orders/{ticket.Id}/start", null)).EnsureSuccessStatusCode();
            (await kitchen.PostAsync($"/api/kitchen/orders/{ticket.Id}/ready", null)).EnsureSuccessStatusCode();
        }

        var served = await PostAsync<OrderDetailDto>(waiter, $"/api/orders/{order.Id}/serve", new { });
        return (served, table);
    }

    private async Task<BillDetailDto> RequestBillAsync(int orderId)
    {
        var waiter = await _factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword);
        return await PostAsync<BillDetailDto>(waiter, $"/api/orders/{orderId}/request-bill", new { });
    }

    private static async Task ClaimAsync(HttpClient client, BillDetailDto bill) =>
        (await client.PostAsync($"/api/billing/{bill.Id}/claim", null)).EnsureSuccessStatusCode();

    private static async Task<BillDetailDto> GetBillAsync(HttpClient client, int id) =>
        (await (await client.GetAsync($"/api/billing/{id}")).ReadEnvelopeAsync<BillDetailDto>()).Data!;

    private static async Task<OrderDetailDto> GetOrderAsync(HttpClient client, int id) =>
        (await (await client.GetAsync($"/api/orders/{id}")).ReadEnvelopeAsync<OrderDetailDto>()).Data!;

    private static async Task<TableDto> TableAsync(HttpClient client, string code) =>
        (await (await client.GetAsync("/api/tables")).ReadEnvelopeAsync<TableMapDto>()).Data!
        .Sections.SelectMany(s => s.Tables).Single(t => t.Code == code);

    private static async Task<BillDetailDto> PayAsync(HttpClient client, BillDetailDto bill, decimal amount)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/billing/{bill.Id}/payments")
        {
            Content = JsonContent.Create(new AddPaymentRequest { PaymentMethodId = 3, Amount = amount, Reference = "UPI-1234567890", RowVersion = bill.RowVersion }, options: PosJson.Options),
        };
        request.Headers.Add(PosHeaders.IdempotencyKey, Guid.NewGuid().ToString());
        var envelope = await (await client.SendAsync(request)).ReadEnvelopeAsync<BillDetailDto>();
        envelope.Success.Should().BeTrue(envelope.Message);
        return envelope.Data!;
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string url, object body) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body, body.GetType(), options: PosJson.Options) });

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body, body.GetType(), options: PosJson.Options) };
        request.Headers.Add(PosHeaders.IdempotencyKey, Guid.NewGuid().ToString());
        var envelope = await (await client.SendAsync(request)).ReadEnvelopeAsync<T>();
        envelope.Success.Should().BeTrue(envelope.Message);
        return envelope.Data!;
    }

    private static async Task<T> PutAsync<T>(HttpClient client, string url, object body)
    {
        var envelope = await (await client.PutAsJsonAsync(url, body, PosJson.Options)).ReadEnvelopeAsync<T>();
        envelope.Success.Should().BeTrue(envelope.Message);
        return envelope.Data!;
    }
}
