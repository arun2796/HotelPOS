using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using HotelPOS.Api.Tests.Support;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Cash;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Orders;
using HotelPOS.Infrastructure.Services;

namespace HotelPOS.Api.Tests;

// One complete restaurant transaction on an empty database (this class has its own, so the day starts at zero and the
// cash drawer at a known float of 1 000): an order with three seat groups, a discount, a split, part and mixed payments,
// a refund, then the day-end figures reconciled against the bills and the payments.
public class ReconciliationApiTests : IClassFixture<ApiFactory>
{
    private const int Cash = 1;
    private const int Card = 2;
    private const int Upi = 3;
    private const decimal OpeningFloat = 1000m;

    private readonly ApiFactory _factory;
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public ReconciliationApiTests(ApiFactory factory, Xunit.Abstractions.ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    [Fact]
    public async Task ASplitDiscountedOrder_PaidInPartsAndThreeWays_AndRefunded_ReconcilesToTheDayEndFigures()
    {
        var waiter = await _factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword);
        var kitchen = await _factory.CreateAuthenticatedClientAsync("kitchen1", ApiFactory.DemoPassword);

        // the cashier's drawer opens with a known float: two 500 notes
        var counterLogin = await _factory.LoginAsync("cashier1", ApiFactory.DemoPassword, deviceName: "RECON-COUNTER");
        var counter = _factory.CreateClient();
        counter.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", counterLogin.AccessToken);
        var notes = (await Get<List<DenominationDto>>(counter, "/api/denominations")).Single(d => d.Value == 500m && d.Kind == DenominationKind.Note);
        var opened = await counter.PostAsJsonAsync("/api/shifts", new OpenShiftRequest { Counts = new[] { new DenominationCountDto { DenominationId = notes.Id, Quantity = 2 } } }, PosJson.Options);
        opened.StatusCode.Should().Be(HttpStatusCode.Created);
        var shift = (await opened.ReadEnvelopeAsync<CashShiftDto>()).Data!;
        shift.FloatAmount.Should().Be(OpeningFloat);
        var manager = await _factory.CreateTerminalClientAsync("manager1", ApiFactory.DemoPassword, "RECON-MANAGER");

        // ---- the order: three seat groups, known prices
        var menu = (await Get<MenuDto>(waiter, "/api/menu")).Items
            .Where(i => i.IsAvailable && i.ModifierGroupIds.Count == 0 && i.Price > 0 && i.StockMode == StockTrackingMode.None).OrderBy(i => i.Id).ToList();
        var (first, second) = (menu[0], menu[1]);
        var table = (await Get<TableMapDto>(waiter, "/api/tables")).Sections.SelectMany(s => s.Tables).Single(t => t.Code == "T01");
        var lines = new[]
        {
            (Item: first, Quantity: 2, Seat: "T01A+B"), (Item: second, Quantity: 1, Seat: "T01A+B"),
            (Item: first, Quantity: 1, Seat: "T01C"), (Item: second, Quantity: 3, Seat: "T01D"),
        };
        var orderedSubtotal = lines.Sum(l => l.Item.Price * l.Quantity);
        var order = await PostJson<OrderDetailDto>(waiter, "/api/orders", new CreateOrderRequest
        {
            TableId = table.Id, GuestCount = 4, Submit = true,
            Items = lines.Select(l => new OrderItemInput { MenuItemId = l.Item.Id, Quantity = l.Quantity, SeatCode = l.Seat }).ToList(),
        });
        foreach (var ticket in order.Tickets)
        {
            (await kitchen.PostAsync($"/api/kitchen/orders/{ticket.Id}/start", null)).EnsureSuccessStatusCode();
            (await kitchen.PostAsync($"/api/kitchen/orders/{ticket.Id}/ready", null)).EnsureSuccessStatusCode();
        }

        await PostJson<OrderDetailDto>(waiter, $"/api/orders/{order.Id}/serve", new { });
        var requested = await PostJson<BillDetailDto>(waiter, $"/api/orders/{order.Id}/request-bill", new { });
        requested.Subtotal.Should().Be(orderedSubtotal, "the bill starts from exactly what was ordered");

        // ---- discount on the whole bill, then split by seat group
        (await counter.PostAsync($"/api/billing/{requested.Id}/claim", null)).EnsureSuccessStatusCode();
        var discounted = await Put<BillDetailDto>(counter, $"/api/billing/{requested.Id}/discount",
            new ApplyDiscountRequest { Type = DiscountType.Percentage, Value = 10m, Reason = "Regular guest", RowVersion = (await Get<BillDetailDto>(counter, $"/api/billing/{requested.Id}")).RowVersion });
        discounted.DiscountAmount.Should().Be(Math.Round(orderedSubtotal * 0.10m, 2, MidpointRounding.AwayFromZero), "10 % of the subtotal, rounded to paise");
        var original = (discounted.Subtotal, discounted.DiscountAmount, discounted.TaxAmount, discounted.GrandTotal);

        var split = await Post(counter, $"/api/billing/{requested.Id}/split", new SplitBillRequest
        {
            Parts = new[]
            {
                new SplitBillPart { BillItemIds = discounted.Items.Where(i => i.SeatCode == "T01C").Select(i => i.Id).ToList() },
                new SplitBillPart { BillItemIds = discounted.Items.Where(i => i.SeatCode == "T01D").Select(i => i.Id).ToList() },
            },
            RowVersion = discounted.RowVersion,
        });
        split.StatusCode.Should().Be(HttpStatusCode.OK);
        var parts = new List<BillDetailDto>();
        foreach (var pending in (await Get<List<BillSummaryDto>>(counter, "/api/billing/pending")).Where(b => b.OrderId == order.Id).OrderBy(b => b.SplitIndex))
        {
            parts.Add(await Get<BillDetailDto>(counter, $"/api/billing/{pending.Id}"));
        }

        var (ab, c, d) = (parts[0], parts[1], parts[2]);
        parts.Select(p => p.Items.Select(i => i.SeatCode).Distinct().Single()).Should().Equal("T01A+B", "T01C", "T01D");

        // original amount against the split bills
        parts.Sum(p => p.Subtotal).Should().Be(original.Subtotal);
        Math.Abs(parts.Sum(p => p.DiscountAmount) - original.DiscountAmount).Should().BeLessThanOrEqualTo(0.03m, "a percentage is rounded once per bill");
        Math.Abs(parts.Sum(p => p.TaxAmount) - original.TaxAmount).Should().BeLessThanOrEqualTo(0.12m, "tax is worked out again from each bill's own lines");
        parts.Should().OnlyContain(p => p.GrandTotal == p.Subtotal - p.DiscountAmount + p.TaxAmount + p.RoundOff);
        foreach (var p in parts)
        {
            _output.WriteLine($"{p.TableCode}: subtotal {p.Subtotal} discount {p.DiscountAmount} tax {p.TaxAmount} round-off {p.RoundOff} total {p.GrandTotal}");
        }

        _output.WriteLine($"original: subtotal {original.Subtotal} discount {original.DiscountAmount} tax {original.TaxAmount} total {original.GrandTotal}; parts total {parts.Sum(p => p.GrandTotal)}");

        // ---- payments: A+B part cash then UPI, C by card, D in cash
        (await Pay(counter, ab, Cash, 100m)).StatusCode.Should().Be(HttpStatusCode.OK);
        var abMid = await Get<BillDetailDto>(counter, $"/api/billing/{ab.Id}");
        (abMid.PaidAmount, abMid.BalanceDue, abMid.PaymentStatus).Should().Be((100m, ab.GrandTotal - 100m, PaymentStatus.PartiallyPaid));
        (await Pay(counter, abMid, Upi, abMid.BalanceDue, "UPI-RECON-1")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Pay(counter, c, Card, c.GrandTotal, "CARD-RECON-1")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Get<TableDto>(waiter, "/api/tables")).Should().NotBeNull();
        (await Pay(counter, d, Cash, d.GrandTotal)).StatusCode.Should().Be(HttpStatusCode.OK);

        // ---- a refund of 10 from bill C's card payment, approved by the manager's terminal
        var cPaid = await Get<BillDetailDto>(manager, $"/api/billing/{c.Id}");
        var refund = await Post(manager, $"/api/billing/{c.Id}/refunds", new RefundRequest
        {
            PaymentId = cPaid.Payments.Single().Id, Amount = 10m, Reason = "Cold food", RowVersion = cPaid.RowVersion,
        });
        refund.StatusCode.Should().Be(HttpStatusCode.OK);

        var final = new List<BillDetailDto>();
        foreach (var p in parts)
        {
            final.Add(await Get<BillDetailDto>(manager, $"/api/billing/{p.Id}"));
        }

        final.Should().OnlyContain(p => p.Status == BillStatus.Settled, "every bill is paid");
        final.Sum(p => p.PaidAmount).Should().Be(parts.Sum(p => p.GrandTotal), "what was taken equals what was billed");
        final.Sum(p => p.RefundedAmount).Should().Be(10m);
        final.Select(p => p.BalanceDue).Should().OnlyContain(b => b == 0m, "nothing is outstanding");
        (await Get<TableMapDto>(waiter, "/api/tables")).Sections.SelectMany(s => s.Tables).Single(t => t.Code == "T01").Status
            .Should().Be(TableStatus.Available, "the table is released once the last bill is paid");

        // ---- the day-end figures
        var precheck = await Get<DayEndPrecheckDto>(manager, "/api/day-end/precheck");
        var summary = precheck.Summary;
        _output.WriteLine($"day end: bills {summary.Bills} gross {summary.GrossSales} discounts {summary.Discounts} tax {summary.Tax} refunds {summary.Refunds} net {summary.NetSales}");
        summary.Bills.Should().Be(3);
        summary.GrossSales.Should().Be(parts.Sum(p => p.GrandTotal), "gross sales = what the three bills came to");
        summary.Discounts.Should().Be(final.Sum(p => p.DiscountAmount));
        summary.Tax.Should().Be(final.Sum(p => p.TaxAmount));
        summary.Refunds.Should().Be(10m);
        summary.NetSales.Should().Be(parts.Sum(p => p.GrandTotal) - 10m, "net sales = gross sales less the refund");
        summary.NetSales.Should().Be(summary.Payments.Sum(p => p.Net), "the money taken, by method, adds up to the net sales");

        var cash = summary.Payments.Single(p => p.Method.Contains("Cash", StringComparison.OrdinalIgnoreCase));
        var upi = summary.Payments.Single(p => p.Method.Contains("UPI", StringComparison.OrdinalIgnoreCase));
        var card = summary.Payments.Single(p => p.Method.Contains("Card", StringComparison.OrdinalIgnoreCase));
        cash.Amount.Should().Be(100m + d.GrandTotal);
        upi.Amount.Should().Be(ab.GrandTotal - 100m);
        (card.Amount, card.RefundAmount, card.Net).Should().Be((c.GrandTotal, 10m, c.GrandTotal - 10m));

        // the drawer: the known float plus the cash taken (no cash refund was paid out)
        var x = await Get<ShiftReportDto>(counter, $"/api/shifts/{shift.Id}/x-report");
        (x.FloatAmount, x.CashSales, x.CashRefunds, x.ExpectedCash).Should().Be((OpeningFloat, 100m + d.GrandTotal, 0m, OpeningFloat + 100m + d.GrandTotal));

        // ---- run the day end: the report pack is written and carries the same figures
        if (PostgresTools.Find("pg_dump", null) is null)
        {
            return;
        }

        var run = await manager.PostAsJsonAsync("/api/day-end", new RunDayEndRequest
        {
            BusinessDay = precheck.BusinessDay,
            Overrides = precheck.BlockingItems.Select(i => new DayEndOverrideDto { Code = i.Code, Reason = "Counters stay open overnight" }).ToList(),
        }, PosJson.Options);
        var result = await run.ReadEnvelopeAsync<DayEndResultDto>();
        result.Success.Should().BeTrue(result.Message);
        result.Data!.PackFiles.Should().NotBeEmpty().And.Contain(f => f.Contains("daily-sales", StringComparison.OrdinalIgnoreCase));
        (result.Data.Summary.Bills, result.Data.Summary.GrossSales, result.Data.Summary.NetSales).Should().Be((3, summary.GrossSales, summary.NetSales));
        _output.WriteLine("report pack: " + string.Join(", ", result.Data.PackFiles));
    }

    private static async Task<T> Get<T>(HttpClient client, string url)
    {
        var envelope = await (await client.GetAsync(url)).ReadEnvelopeAsync<T>();
        envelope.Success.Should().BeTrue($"{url}: {envelope.Message}");
        return envelope.Data!;
    }

    private static async Task<T> Put<T>(HttpClient client, string url, object body)
    {
        var envelope = await (await client.PutAsJsonAsync(url, body, PosJson.Options)).ReadEnvelopeAsync<T>();
        envelope.Success.Should().BeTrue($"{url}: {envelope.Message}");
        return envelope.Data!;
    }

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

    private static Task<HttpResponseMessage> Pay(HttpClient client, BillDetailDto bill, int method, decimal amount, string? reference = null) =>
        Post(client, $"/api/billing/{bill.Id}/payments", new AddPaymentRequest { PaymentMethodId = method, Amount = amount, Reference = reference, RowVersion = bill.RowVersion });
}
