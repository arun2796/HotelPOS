using System.Net.Http.Json;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Orders;

namespace HotelPOS.Api.Tests.Support;

// Steps the cash and Day End API tests share: a served order on a demo table, its bill, a payment.
public static class ApiScenario
{
    public static async Task<BillDetailDto> BillForAsync(ApiFactory factory, string tableCode, string item = "Masala Chai")
    {
        var waiter = await factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword);
        var map = (await (await waiter.GetAsync("/api/tables")).ReadEnvelopeAsync<TableMapDto>()).Data!;
        var menu = (await (await waiter.GetAsync("/api/menu")).ReadEnvelopeAsync<MenuDto>()).Data!;
        var entry = menu.Items.Single(i => i.Name == item);
        var required = menu.ModifierGroups.Where(g => entry.ModifierGroupIds.Contains(g.Id) && g.MinSelections > 0).Select(g => g.Options.First().Id);
        var create = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
        {
            Content = JsonContent.Create(new CreateOrderRequest
            {
                TableId = map.Sections.SelectMany(s => s.Tables).Single(t => t.Code == tableCode).Id,
                GuestCount = 1,
                Submit = true,
                Items = new[] { new OrderItemInput { MenuItemId = entry.Id, Quantity = 1, ModifierOptionIds = required.ToList() } },
            }, options: PosJson.Options),
        };
        create.Headers.Add(PosHeaders.IdempotencyKey, Guid.NewGuid().ToString());
        var order = (await (await waiter.SendAsync(create)).ReadEnvelopeAsync<OrderDetailDto>()).Data!;

        var kitchen = await factory.CreateAuthenticatedClientAsync("kitchen1", ApiFactory.DemoPassword);
        foreach (var ticket in order.Tickets)
        {
            (await kitchen.PostAsync($"/api/kitchen/orders/{ticket.Id}/start", null)).EnsureSuccessStatusCode();
            (await kitchen.PostAsync($"/api/kitchen/orders/{ticket.Id}/ready", null)).EnsureSuccessStatusCode();
        }

        (await (await waiter.PostAsync($"/api/orders/{order.Id}/serve", null)).ReadEnvelopeAsync<OrderDetailDto>()).Data!.Status.Should().Be(OrderStatus.Served);
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{order.Id}/request-bill");
        request.Headers.Add(PosHeaders.IdempotencyKey, Guid.NewGuid().ToString());
        var bill = await (await waiter.SendAsync(request)).ReadEnvelopeAsync<BillDetailDto>();
        bill.Success.Should().BeTrue(bill.Message);
        return bill.Data!;
    }

    public static Task<HttpResponseMessage> PayAsync(HttpClient client, BillDetailDto bill, int methodId, string? reference = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/billing/{bill.Id}/payments")
        {
            Content = JsonContent.Create(new AddPaymentRequest
            {
                PaymentMethodId = methodId,
                Amount = bill.GrandTotal,
                Reference = reference,
                RowVersion = bill.RowVersion,
            }, options: PosJson.Options),
        };
        request.Headers.Add(PosHeaders.IdempotencyKey, Guid.NewGuid().ToString());
        return client.SendAsync(request);
    }
}
