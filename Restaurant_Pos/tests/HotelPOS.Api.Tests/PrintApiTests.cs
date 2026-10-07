using System.Net;
using System.Net.Http.Json;
using HotelPOS.Api.Tests.Support;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Orders;
using HotelPOS.Contracts.Print;
using HotelPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HotelPOS.Api.Tests;

public class PrintApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public PrintApiTests(ApiFactory factory)
    {
        _factory = factory;
    }

    public static TheoryData<string, string, string, HttpStatusCode> Matrix => new()
    {
        { "kitchen1", "GET", "/api/print/invoice/1", HttpStatusCode.Forbidden },
        { "kitchen1", "GET", "/api/print/receipt/1", HttpStatusCode.Forbidden },
        { "kitchen1", "POST", "/api/print/reprints", HttpStatusCode.Forbidden },
        { "waiter1", "GET", "/api/print/receipt/1", HttpStatusCode.Forbidden },
        { "cashier1", "GET", "/api/print/kot/1", HttpStatusCode.Forbidden },
        { "kitchen1", "GET", "/api/print/kot/999999", HttpStatusCode.NotFound },
        { "cashier1", "GET", "/api/print/invoice/999999", HttpStatusCode.NotFound },
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
    public async Task Documents_AreBuiltFromTheServer_AndReprintsAreAudited()
    {
        var waiter = await _factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword);
        var kitchen = await _factory.CreateAuthenticatedClientAsync("kitchen1", ApiFactory.DemoPassword);
        var cashier = await _factory.CreateAuthenticatedClientAsync("cashier1", ApiFactory.DemoPassword);
        var order = await SendOrderAsync(waiter, "T13", "Chicken Biryani");

        var kot = await (await kitchen.GetAsync($"/api/print/kot/{order.Tickets.Single().Id}")).ReadEnvelopeAsync<KitchenTicketDocument>();
        kot.Data!.TicketNumber.Should().Be(order.Tickets.Single().TicketNumber);
        kot.Data.Items.Should().ContainSingle().Which.Name.Should().Be("Chicken Biryani");

        foreach (var ticket in order.Tickets)
        {
            (await kitchen.PostAsync($"/api/kitchen/orders/{ticket.Id}/start", null)).EnsureSuccessStatusCode();
            (await kitchen.PostAsync($"/api/kitchen/orders/{ticket.Id}/ready", null)).EnsureSuccessStatusCode();
        }

        (await waiter.PostAsync($"/api/orders/{order.Id}/serve", null)).EnsureSuccessStatusCode();
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{order.Id}/request-bill");
        request.Headers.Add(PosHeaders.IdempotencyKey, Guid.NewGuid().ToString());
        var bill = (await (await waiter.SendAsync(request)).ReadEnvelopeAsync<BillDetailDto>()).Data!;
        var pay = new HttpRequestMessage(HttpMethod.Post, $"/api/billing/{bill.Id}/payments")
        {
            Content = JsonContent.Create(new AddPaymentRequest { PaymentMethodId = 1, Amount = bill.GrandTotal, Tendered = bill.GrandTotal + 50, RowVersion = bill.RowVersion }, options: PosJson.Options),
        };
        pay.Headers.Add(PosHeaders.IdempotencyKey, Guid.NewGuid().ToString());
        var paid = (await (await cashier.SendAsync(pay)).ReadEnvelopeAsync<BillDetailDto>()).Data!;

        var invoice = await (await cashier.GetAsync($"/api/print/invoice/{bill.Id}")).ReadEnvelopeAsync<InvoiceDocument>();
        invoice.Data!.InvoiceNumber.Should().Be(paid.InvoiceNumber);
        invoice.Data.RestaurantName.Should().NotBeEmpty();
        invoice.Data.GrandTotal.Should().Be(bill.GrandTotal);
        invoice.Data.Payments.Should().ContainSingle();
        var receipt = await (await cashier.GetAsync($"/api/print/receipt/{paid.Payments.Single().Id}")).ReadEnvelopeAsync<ReceiptDocument>();
        receipt.Data!.ChangeAmount.Should().Be(50m);

        var reprint = await cashier.PostAsJsonAsync("/api/print/reprints",
            new ReprintRequest { DocumentType = PrintDocumentType.Invoice, EntityId = bill.Id, Reason = "Customer asked for a copy" }, PosJson.Options);
        reprint.StatusCode.Should().Be(HttpStatusCode.OK);
        var noReason = await cashier.PostAsJsonAsync("/api/print/reprints", new ReprintRequest { DocumentType = PrintDocumentType.Invoice, EntityId = bill.Id }, PosJson.Options);
        noReason.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var audit = await db.AuditLogs.Where(a => a.Action == "Print.Reprint" && a.EntityId == bill.Id.ToString()).ToListAsync();
        audit.Should().ContainSingle().Which.NewValues.Should().Contain("Customer asked for a copy");
    }

    private static async Task<OrderDetailDto> SendOrderAsync(HttpClient waiter, string tableCode, params string[] items)
    {
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
        var envelope = await (await waiter.SendAsync(request)).ReadEnvelopeAsync<OrderDetailDto>();
        envelope.Success.Should().BeTrue(envelope.Message);
        return envelope.Data!;
    }
}
