using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HotelPOS.Api.Common;
using HotelPOS.Api.Tests.Support;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Orders;
using HotelPOS.Contracts.Realtime;
using Microsoft.AspNetCore.SignalR.Client;

namespace HotelPOS.Api.Tests;

public class BillingApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public BillingApiTests(ApiFactory factory)
    {
        _factory = factory;
    }

    public static TheoryData<string, string, string, HttpStatusCode> Matrix => new()
    {
        { "cashier1", "POST", "/api/menu-items", HttpStatusCode.Forbidden },
        { "cashier1", "GET", "/api/billing/pending", HttpStatusCode.OK },
        { "kitchen1", "GET", "/api/billing/pending", HttpStatusCode.Forbidden },
        { "kitchen1", "POST", "/api/billing/1/claim", HttpStatusCode.Forbidden },
        { "kitchen1", "POST", "/api/billing/1/payments", HttpStatusCode.Forbidden },
        { "kitchen1", "POST", "/api/orders/1/request-bill", HttpStatusCode.Forbidden },
        { "waiter1", "GET", "/api/billing/closed", HttpStatusCode.Forbidden },
        { "waiter1", "POST", "/api/billing/1/refunds", HttpStatusCode.Forbidden },
        { "cashier1", "GET", "/api/discounts", HttpStatusCode.OK },
        { "cashier1", "POST", "/api/discounts", HttpStatusCode.Forbidden },
        { "waiter1", "GET", "/api/payment-methods", HttpStatusCode.OK },
        { "manager1", "POST", "/api/payment-methods", HttpStatusCode.Forbidden },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Endpoint_ReturnsExpectedStatus_ForRole(string username, string method, string url, HttpStatusCode expected)
    {
        var client = await _factory.CreateAuthenticatedClientAsync(username, ApiFactory.DemoPassword);

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url) { Content = JsonContent.Create(new { }) });

        response.StatusCode.Should().Be(expected);
    }

    [Fact]
    public async Task RequestBill_ReachesTheCounter_AndPaymentCompletedReachesTheWaiter()
    {
        var cashierLogin = await _factory.LoginAsync("cashier1", ApiFactory.DemoPassword, deviceName: "BILLING-01");
        var waiterLogin = await _factory.LoginAsync("waiter1", ApiFactory.DemoPassword, deviceName: "WAITER-01");
        await using var cashierHub = _factory.CreateHubConnection(cashierLogin.AccessToken);
        await using var waiterHub = _factory.CreateHubConnection(waiterLogin.AccessToken);
        var requested = new TaskCompletionSource<BillRequestedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource<PaymentCompletedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        cashierHub.On<BillRequestedEvent>(HubEvents.BillRequested, e => requested.TrySetResult(e));
        waiterHub.On<PaymentCompletedEvent>(HubEvents.PaymentCompleted, e => completed.TrySetResult(e));
        await cashierHub.StartAsync();
        await waiterHub.StartAsync();

        var order = await ServedOrderAsync("T05", "Chicken Biryani");
        var bill = await RequestBillAsync(order.Id);
        var notice = await requested.Task.WaitAsync(TimeSpan.FromSeconds(15));
        notice.BillId.Should().Be(bill.Id);
        notice.TableCode.Should().Be("T05");

        var cashier = Client(cashierLogin);
        var key = Guid.NewGuid();
        var upi = await (await PayAsync(cashier, bill, 3, bill.GrandTotal, key, "UPI-1234567890")).ReadEnvelopeAsync<BillDetailDto>();
        upi.Data!.Status.Should().Be(BillStatus.Settled);
        upi.Data.InvoiceNumber.Should().StartWith("INV-");
        var paid = await completed.Task.WaitAsync(TimeSpan.FromSeconds(15));
        paid.TableCode.Should().Be("T05");
        paid.InvoiceNumber.Should().Be(upi.Data.InvoiceNumber);

        var replay = await PayAsync(cashier, bill, 3, bill.GrandTotal, key, "UPI-1234567890");
        replay.Headers.GetValues(IdempotencyMiddleware.ReplayedHeader).Should().Equal("true");
        var after = await (await cashier.GetAsync($"/api/billing/{bill.Id}")).ReadEnvelopeAsync<BillDetailDto>();
        after.Data!.Payments.Should().ContainSingle();
        var waiter = Client(waiterLogin);
        var table = (await (await waiter.GetAsync($"/api/tables/{order.TableId}")).ReadEnvelopeAsync<TableDetailDto>()).Data!;
        table.Table.Status.Should().Be(TableStatus.Available);
    }

    [Fact]
    public async Task TwoCountersSettlingTheSameBill_OnlyOneSucceeds()
    {
        var counterOne = Client(await _factory.LoginAsync("cashier1", ApiFactory.DemoPassword, deviceName: "BILLING-01"));
        var counterTwo = Client(await _factory.LoginAsync("cashier1", ApiFactory.DemoPassword, deviceName: "BILLING-02"));
        var order = await ServedOrderAsync("T06", "Chicken Biryani");
        var bill = await RequestBillAsync(order.Id);

        var responses = await Task.WhenAll(
            PayAsync(counterOne, bill, 1, bill.GrandTotal, Guid.NewGuid()),
            PayAsync(counterTwo, bill, 1, bill.GrandTotal, Guid.NewGuid()));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        var loser = await responses.Single(r => r.StatusCode != HttpStatusCode.OK).ReadEnvelopeAsync<BillDetailDto>();
        loser.Errors.Single().Code.Should().BeOneOf(ErrorCodes.BillClaimed, ErrorCodes.ConcurrencyConflict);
        var final = await (await counterOne.GetAsync($"/api/billing/{bill.Id}")).ReadEnvelopeAsync<BillDetailDto>();
        final.Data!.Payments.Should().ContainSingle();
        final.Data.PaidAmount.Should().Be(bill.GrandTotal);
    }

    [Fact]
    public async Task ASecondCounter_SeesWhoHoldsTheBill()
    {
        var counterOne = Client(await _factory.LoginAsync("cashier1", ApiFactory.DemoPassword, deviceName: "BILLING-01"));
        var counterTwo = Client(await _factory.LoginAsync("cashier1", ApiFactory.DemoPassword, deviceName: "BILLING-02"));
        var bill = await RequestBillAsync((await ServedOrderAsync("T07", "Chicken Biryani")).Id);

        (await counterOne.PostAsJsonAsync($"/api/billing/{bill.Id}/claim", new ClaimBillRequest(), PosJson.Options)).StatusCode.Should().Be(HttpStatusCode.OK);
        var conflict = await counterTwo.PostAsJsonAsync($"/api/billing/{bill.Id}/claim", new ClaimBillRequest(), PosJson.Options);
        var pending = await (await counterTwo.GetAsync("/api/billing/pending")).ReadEnvelopeAsync<List<BillSummaryDto>>();

        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await conflict.ReadEnvelopeAsync<BillDetailDto>()).Message.Should().Contain("BILLING-01");
        pending.Data!.Single(b => b.Id == bill.Id).ClaimedByDevice.Should().Be("BILLING-01");
    }

    private HttpClient Client(Contracts.Auth.LoginResponse login)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return client;
    }

    private static Task<HttpResponseMessage> PayAsync(HttpClient client, BillDetailDto bill, int methodId, decimal amount, Guid key, string? reference = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/billing/{bill.Id}/payments")
        {
            Content = JsonContent.Create(new AddPaymentRequest
            {
                PaymentMethodId = methodId,
                Amount = amount,
                Reference = reference,
                RowVersion = bill.RowVersion,
            }, options: PosJson.Options),
        };
        request.Headers.Add(PosHeaders.IdempotencyKey, key.ToString());
        return client.SendAsync(request);
    }

    private async Task<BillDetailDto> RequestBillAsync(int orderId)
    {
        var waiter = await _factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword);
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{orderId}/request-bill");
        request.Headers.Add(PosHeaders.IdempotencyKey, Guid.NewGuid().ToString());
        var envelope = await (await waiter.SendAsync(request)).ReadEnvelopeAsync<BillDetailDto>();
        envelope.Success.Should().BeTrue(envelope.Message);
        return envelope.Data!;
    }

    private async Task<OrderDetailDto> ServedOrderAsync(string tableCode, params string[] items)
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
                    var required = menu.ModifierGroups.Where(g => entry.ModifierGroupIds.Contains(g.Id) && g.MinSelections > 0)
                        .Select(g => g.Options.First().Id);
                    return new OrderItemInput { MenuItemId = entry.Id, Quantity = 1, ModifierOptionIds = required.ToList() };
                }).ToList(),
            }, options: PosJson.Options),
        };
        request.Headers.Add(PosHeaders.IdempotencyKey, Guid.NewGuid().ToString());
        var order = (await (await waiter.SendAsync(request)).ReadEnvelopeAsync<OrderDetailDto>()).Data!;

        var kitchen = await _factory.CreateAuthenticatedClientAsync("kitchen1", ApiFactory.DemoPassword);
        foreach (var ticket in order.Tickets)
        {
            (await kitchen.PostAsync($"/api/kitchen/orders/{ticket.Id}/start", null)).EnsureSuccessStatusCode();
            (await kitchen.PostAsync($"/api/kitchen/orders/{ticket.Id}/ready", null)).EnsureSuccessStatusCode();
        }

        var served = await (await waiter.PostAsync($"/api/orders/{order.Id}/serve", null)).ReadEnvelopeAsync<OrderDetailDto>();
        served.Data!.Status.Should().Be(OrderStatus.Served);
        return served.Data;
    }
}
