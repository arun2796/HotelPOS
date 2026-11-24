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
using HotelPOS.Contracts.Print;
using HotelPOS.Contracts.Realtime;
using Microsoft.AspNetCore.SignalR.Client;
using System.Collections.Concurrent;
using HotelPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HotelPOS.Api.Tests;

// Production checks for seat groups on merged tables: merge and unmerge rules, payments of separate bills, kitchen printing,
// and tickets created before seat groups existed. Every test makes its own tables so nothing depends on the demo data.
public class SeatGroupProductionApiTests : IClassFixture<ApiFactory>
{
    private const int Cash = 1;
    private const int Card = 2;
    private const int Upi = 3;

    private readonly ApiFactory _factory;

    public SeatGroupProductionApiTests(ApiFactory factory)
    {
        _factory = factory;
    }

    // ---- merged tables ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task MergedTables_KeepSeatCodesUnique_AllowGuestsUpToTheCombinedSeats_AndOrdersSpanBothTables()
    {
        var waiter = await _factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword);
        var primary = await NewTableAsync("MA", 4);
        var companion = await NewTableAsync("MB", 6);
        var item = await ItemAsync(waiter);
        await OccupyAndMergeAsync(waiter, primary, companion, guests: 4);

        var a = await LookupAsync(waiter, "MA");
        var b = await LookupAsync(waiter, "MB");
        a.SeatCodes.Concat(b.SeatCodes).Should().OnlyHaveUniqueItems("seat codes carry the table code, so merging cannot create a duplicate");
        a.SeatCodes.Concat(b.SeatCodes).Should().HaveCount(10);
        (a.Id, b.Id, a.Code, b.Code).Should().Be((primary.Id, companion.Id, "MA", "MB"), "the original tables keep their identity");
        b.MergedIntoTableId.Should().Be(primary.Id);

        // ten guests fit the combined seats, eleven do not
        (await Post(waiter, "/api/orders", new CreateOrderRequest { TableId = primary.Id, GuestCount = 11, Submit = true, Items = new[] { new OrderItemInput { MenuItemId = item.Id, SeatCode = "MAA" } } }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var order = await PostJson<OrderDetailDto>(waiter, "/api/orders", new CreateOrderRequest
        {
            TableId = primary.Id, GuestCount = 10, Submit = true,
            Items = new[]
            {
                new OrderItemInput { MenuItemId = item.Id, SeatCode = "MAA+B" },
                new OrderItemInput { MenuItemId = item.Id, SeatCode = "MBA+B+C" },
                new OrderItemInput { MenuItemId = item.Id, SeatCode = "MAD+MBD" },
            },
        });
        order.SeatGroups.Select(g => g.SeatCode).Should().Equal("MAA+B", "MAD+MBD", "MBA+B+C");

        // seat ownership holds across the two original tables: MBD belongs to MAD+MBD, so it cannot start a group of its own
        (await Post(waiter, $"/api/orders/{order.Id}/items", new AppendOrderItemsRequest { Items = new[] { new OrderItemInput { MenuItemId = item.Id, SeatCode = "MBD" } } }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Post(waiter, $"/api/orders/{order.Id}/items", new AppendOrderItemsRequest { Items = new[] { new OrderItemInput { MenuItemId = item.Id, SeatCode = "MAC" } } }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task MergeRules_RefuseRepeatedAndInvalidMemberships_AndKeepTheTables()
    {
        var waiter = await _factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword);
        var c = await NewTableAsync("MC", 4);
        var d = await NewTableAsync("MD", 4);
        var e = await NewTableAsync("ME", 4);
        var f = await NewTableAsync("MF", 4);

        // the main table needs seated guests first
        (await Post(waiter, $"/api/tables/{c.Id}/merge", new MergeTablesRequest { CompanionTableId = d.Id })).StatusCode.Should().NotBe(HttpStatusCode.OK);
        await PostJson<TableDto>(waiter, $"/api/tables/{c.Id}/occupy", new OccupyTableRequest { GuestCount = 2, RowVersion = c.RowVersion });

        (await Post(waiter, $"/api/tables/{c.Id}/merge", new MergeTablesRequest { CompanionTableId = c.Id })).StatusCode.Should().NotBe(HttpStatusCode.OK, "a table cannot merge with itself");
        (await Post(waiter, $"/api/tables/{c.Id}/merge", new MergeTablesRequest { CompanionTableId = d.Id })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Post(waiter, $"/api/tables/{c.Id}/merge", new MergeTablesRequest { CompanionTableId = d.Id })).StatusCode.Should().NotBe(HttpStatusCode.OK, "a repeated merge must not duplicate the membership");

        // a table that is already a member cannot join another group, nor take members of its own
        await PostJson<TableDto>(waiter, $"/api/tables/{e.Id}/occupy", new OccupyTableRequest { GuestCount = 2, RowVersion = e.RowVersion });
        (await Post(waiter, $"/api/tables/{e.Id}/merge", new MergeTablesRequest { CompanionTableId = d.Id })).StatusCode.Should().NotBe(HttpStatusCode.OK);
        (await Post(waiter, $"/api/tables/{d.Id}/merge", new MergeTablesRequest { CompanionTableId = f.Id })).StatusCode.Should().NotBe(HttpStatusCode.OK);

        // a table with guests cannot be merged in
        await PostJson<TableDto>(waiter, $"/api/tables/{f.Id}/occupy", new OccupyTableRequest { GuestCount = 1, RowVersion = f.RowVersion });
        (await Post(waiter, $"/api/tables/{c.Id}/merge", new MergeTablesRequest { CompanionTableId = f.Id })).StatusCode.Should().NotBe(HttpStatusCode.OK);

        var after = await LookupAsync(waiter, "MC");
        after.MergedTableCodes.Should().Equal("MD");
        (await LookupAsync(waiter, "ME")).MergedTableCodes.Should().BeEmpty();
    }

    [Fact]
    public async Task Unmerge_KeepsTheOrderAndItsHistory_ButNotWhenGuestsWouldNotFit()
    {
        var waiter = await _factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword);
        var primary = await NewTableAsync("MG", 4);
        var companion = await NewTableAsync("MH", 4);
        var item = await ItemAsync(waiter);
        await OccupyAndMergeAsync(waiter, primary, companion, guests: 4);
        var order = await PostJson<OrderDetailDto>(waiter, "/api/orders", new CreateOrderRequest
        {
            TableId = primary.Id, GuestCount = 6, Submit = true,
            Items = new[] { new OrderItemInput { MenuItemId = item.Id, Quantity = 2, SeatCode = "MGA+B" } },
        });

        // six guests need both tables
        var tooMany = await Post(waiter, $"/api/tables/{companion.Id}/unmerge", new { });
        tooMany.StatusCode.Should().NotBe(HttpStatusCode.OK);
        (await tooMany.Content.ReadAsStringAsync()).Should().Contain("guests");
        (await LookupAsync(waiter, "MH")).MergedIntoTableId.Should().Be(primary.Id);

        // lower the guest count, then the second table can go back
        var current = (await GetJsonAsync<OrderDetailDto>(waiter, $"/api/orders/{order.Id}"));
        (await Put(waiter, $"/api/orders/{order.Id}", new UpdateOrderRequest { GuestCount = 3, RowVersion = current.RowVersion })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Post(waiter, $"/api/tables/{companion.Id}/unmerge", new { })).StatusCode.Should().Be(HttpStatusCode.OK);

        var freed = await LookupAsync(waiter, "MH");
        (freed.Status, freed.MergedIntoTableId).Should().Be((TableStatus.Available, (int?)null));
        (await LookupAsync(waiter, "MG")).MergedTableCodes.Should().BeEmpty();
        var after = await GetJsonAsync<OrderDetailDto>(waiter, $"/api/orders/{order.Id}");
        after.Items.Select(i => (i.SeatCode, i.Quantity)).Should().Equal(order.Items.Select(i => (i.SeatCode, i.Quantity)), "the order history is untouched");
        after.Tickets.Select(t => t.Id).Should().Equal(order.Tickets.Select(t => t.Id));
    }

    [Fact]
    public async Task Unmerge_AfterAPartialPayment_KeepsThePaymentHistory_AndTheBillCanBeFinished()
    {
        var waiter = await _factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword);
        var kitchen = await _factory.CreateAuthenticatedClientAsync("kitchen1", ApiFactory.DemoPassword);
        var cashier = await _factory.CreateTerminalClientAsync("cashier1", ApiFactory.DemoPassword, "BILLING-01");
        var primary = await NewTableAsync("MI", 4);
        var companion = await NewTableAsync("MJ", 4);
        var item = await ItemAsync(waiter);
        await OccupyAndMergeAsync(waiter, primary, companion, guests: 2);
        var order = await PostJson<OrderDetailDto>(waiter, "/api/orders", new CreateOrderRequest
        {
            TableId = primary.Id, GuestCount = 2, Submit = true,
            Items = new[] { new OrderItemInput { MenuItemId = item.Id, Quantity = 2, SeatCode = "MIA+B" } },
        });
        var bill = await ServeAndBillAsync(waiter, kitchen, order);

        var half = Math.Round(bill.GrandTotal / 2, 2);
        var partial = await PayAsync(cashier, bill, Cash, half);
        partial.StatusCode.Should().Be(HttpStatusCode.OK);

        (await Post(waiter, $"/api/tables/{companion.Id}/unmerge", new { })).StatusCode.Should().Be(HttpStatusCode.OK, "the second table's seats are not used");
        var mid = await GetJsonAsync<BillDetailDto>(cashier, $"/api/billing/{bill.Id}");
        mid.Payments.Should().ContainSingle(p => p.Amount == half, "the payment taken before the unmerge is still there");
        mid.PaymentStatus.Should().Be(PaymentStatus.PartiallyPaid);
        (await LookupAsync(waiter, "MI")).Status.Should().NotBe(TableStatus.Available, "the table is still owed money");

        (await PayAsync(cashier, mid, Upi, mid.BalanceDue, "UPI-OK-1")).StatusCode.Should().Be(HttpStatusCode.OK);
        var done = await GetJsonAsync<BillDetailDto>(cashier, $"/api/billing/{bill.Id}");
        (done.Status, done.Payments.Count).Should().Be((BillStatus.Settled, 2));
        (await LookupAsync(waiter, "MI")).Status.Should().Be(TableStatus.Available);
    }

    // ---- separate bills: payments, refund, reprint --------------------------------------------------------------------

    [Fact]
    public async Task SeparateBills_TakePartialAndMixedPayments_KeepHistory_RefundAndReprintCorrectly()
    {
        var waiter = await _factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword);
        var kitchen = await _factory.CreateAuthenticatedClientAsync("kitchen1", ApiFactory.DemoPassword);
        var cashier = await _factory.CreateTerminalClientAsync("cashier1", ApiFactory.DemoPassword, "BILLING-01");
        var manager = await _factory.CreateTerminalClientAsync("manager1", ApiFactory.DemoPassword, "BILLING-02");
        var table = await NewTableAsync("BP", 4);
        var (first, second) = await TwoItemsAsync(waiter);

        var order = await PostJson<OrderDetailDto>(waiter, "/api/orders", new CreateOrderRequest
        {
            TableId = table.Id, GuestCount = 4, Submit = true,
            Items = new[]
            {
                new OrderItemInput { MenuItemId = first.Id, Quantity = 2, SeatCode = "BPA+B" },
                new OrderItemInput { MenuItemId = second.Id, SeatCode = "BPA+B" },
                new OrderItemInput { MenuItemId = first.Id, SeatCode = "BPC" },
                new OrderItemInput { MenuItemId = second.Id, Quantity = 3, SeatCode = "BPD" },
            },
        });
        foreach (var ticket in order.Tickets)
        {
            (await kitchen.PostAsync($"/api/kitchen/orders/{ticket.Id}/start", null)).EnsureSuccessStatusCode();
            (await kitchen.PostAsync($"/api/kitchen/orders/{ticket.Id}/ready", null)).EnsureSuccessStatusCode();
        }

        await PostJson<OrderDetailDto>(waiter, $"/api/orders/{order.Id}/serve", new { });
        await PostJson<BillDetailDto>(waiter, $"/api/orders/{order.Id}/request-bill?splitBySeat=true", new { });
        var summaries = (await GetJsonAsync<List<BillSummaryDto>>(cashier, "/api/billing/pending")).Where(b => b.OrderId == order.Id).OrderBy(b => b.SplitIndex).ToList();
        summaries.Should().HaveCount(3);
        var bills = new List<BillDetailDto>();
        foreach (var summary in summaries)
        {
            bills.Add(await GetJsonAsync<BillDetailDto>(cashier, $"/api/billing/{summary.Id}"));
        }

        // each bill holds one seat group, and only its own dishes
        bills.Select(b => b.Items.Select(i => i.SeatCode).Distinct().Single()).Should().Equal("BPA+B", "BPC", "BPD");
        bills.SelectMany(b => b.Items).Sum(i => i.Quantity).Should().Be(2 + 1 + 1 + 3);
        bills.All(b => b.GrandTotal == b.Subtotal - b.DiscountAmount + b.TaxAmount + b.RoundOff).Should().BeTrue("each bill adds up under the billing rules");

        // bill A+B: a part payment in cash, the rest by UPI
        var ab = bills[0];
        (await PayAsync(cashier, ab, Cash, 100m)).StatusCode.Should().Be(HttpStatusCode.OK);
        var abMid = await GetJsonAsync<BillDetailDto>(cashier, $"/api/billing/{ab.Id}");
        abMid.PaymentStatus.Should().Be(PaymentStatus.PartiallyPaid);
        (await PayAsync(cashier, abMid, Upi, abMid.BalanceDue, "UPI-AB-1")).StatusCode.Should().Be(HttpStatusCode.OK);
        var abDone = await GetJsonAsync<BillDetailDto>(cashier, $"/api/billing/{ab.Id}");
        abDone.Status.Should().Be(BillStatus.Settled);
        abDone.Payments.Select(p => (p.PaymentMethodId, p.Amount)).Should().Equal(new[] { (Cash, 100m), (Upi, abMid.BalanceDue) }, "both payments stay in the history");

        // bill C paid by card in full; the table is still owed bill D
        (await PayAsync(cashier, bills[1], Card, bills[1].GrandTotal, "CARD-C-1")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await LookupAsync(waiter, "BP")).Status.Should().NotBe(TableStatus.Available, "bill D is unpaid, so the table cannot be released");
        (await PayAsync(cashier, bills[2], Cash, bills[2].GrandTotal)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await LookupAsync(waiter, "BP")).Status.Should().Be(TableStatus.Available);

        // a partial refund of bill D keeps the original payment and adds the refund beside it
        var dPaid = await GetJsonAsync<BillDetailDto>(manager, $"/api/billing/{bills[2].Id}");
        var refund = await Post(manager, $"/api/billing/{dPaid.Id}/refunds", new RefundRequest
        {
            PaymentId = dPaid.Payments.Single().Id, Amount = 10m, Reason = "Cold food", RowVersion = dPaid.RowVersion,
        });
        refund.StatusCode.Should().Be(HttpStatusCode.OK);
        var dAfter = await GetJsonAsync<BillDetailDto>(manager, $"/api/billing/{dPaid.Id}");
        dAfter.RefundedAmount.Should().Be(10m);
        dAfter.Payments.Should().HaveCount(2).And.Contain(p => p.RefundOfPaymentId == null && p.Amount == dPaid.GrandTotal).And.Contain(p => p.RefundOfPaymentId != null);

        // reprints: each invoice shows its own seat group's dishes and its own total
        foreach (var bill in bills)
        {
            var invoice = await GetJsonAsync<InvoiceDocument>(cashier, $"/api/print/invoice/{bill.Id}");
            invoice.GrandTotal.Should().Be(bill.GrandTotal);
            invoice.Lines.Sum(l => l.Quantity).Should().Be(bill.Items.Sum(i => i.Quantity));
            invoice.Lines.Select(l => l.Name).Should().BeEquivalentTo(bill.Items.Select(i => i.ItemName));
        }
    }

    // ---- kitchen printing and old tickets -----------------------------------------------------------------------------

    [Fact]
    public async Task KitchenTickets_PrintTheTable_MergedTables_AndSeatGroup_OnlyTheirOwnDishes_AndAdditionsOnlyWhatIsNew()
    {
        var waiter = await _factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword);
        var kitchen = await _factory.CreateAuthenticatedClientAsync("kitchen1", ApiFactory.DemoPassword);
        var primary = await NewTableAsync("KA", 4);
        var companion = await NewTableAsync("KB", 4);
        var (first, second) = await TwoItemsAsync(waiter);
        await OccupyAndMergeAsync(waiter, primary, companion, guests: 4);
        var order = await PostJson<OrderDetailDto>(waiter, "/api/orders", new CreateOrderRequest
        {
            TableId = primary.Id, GuestCount = 6, Submit = true,
            Items = new[]
            {
                new OrderItemInput { MenuItemId = first.Id, Quantity = 2, SeatCode = "KAA+B" },
                new OrderItemInput { MenuItemId = second.Id, SeatCode = "KBC" },
            },
        });
        order.Tickets.Should().HaveCount(2);

        var documents = new List<KitchenTicketDocument>();
        foreach (var ticket in order.Tickets)
        {
            documents.Add(await GetJsonAsync<KitchenTicketDocument>(kitchen, $"/api/print/kot/{ticket.Id}"));
        }

        documents.Should().OnlyContain(d => d.TableCode == "KA" && d.MergedWith == "KB", "the table and the merged table are on every ticket");
        var ab = documents.Single(d => d.SeatGroup == "KAA+B");
        var c = documents.Single(d => d.SeatGroup == "KBC");
        ab.Items.Select(i => (i.Name, i.Quantity, i.SeatCode)).Should().Equal((first.Name, 2, "KAA+B"));
        c.Items.Select(i => (i.Name, i.Quantity, i.SeatCode)).Should().Equal((second.Name, 1, "KBC"));
        ab.Items.Single().Notes.Should().Contain("Seat KAA+B", "every dish names its seat");

        // the PDF of each ticket is a real document (written to the temp folder so it can be opened and read)
        var pdfFolder = Path.Combine(Path.GetTempPath(), "hotelpos-kot-pdf");
        Directory.CreateDirectory(pdfFolder);
        foreach (var ticket in order.Tickets)
        {
            var pdf = await kitchen.GetByteArrayAsync($"/api/print/kot/{ticket.Id}.pdf");
            System.Text.Encoding.ASCII.GetString(pdf, 0, 4).Should().Be("%PDF");
            await File.WriteAllBytesAsync(Path.Combine(pdfFolder, $"kot-{ticket.TicketNumber}.pdf"), pdf);
        }

        // dishes added later print on a ticket of their own, with only what is new
        var added = await PostJson<OrderDetailDto>(waiter, $"/api/orders/{order.Id}/items", new AppendOrderItemsRequest
        {
            Items = new[] { new OrderItemInput { MenuItemId = second.Id, Quantity = 2, SeatCode = "KAA+B" } },
        });
        var newTicket = added.Tickets.Single(t => !order.Tickets.Any(o => o.Id == t.Id));
        var addition = await GetJsonAsync<KitchenTicketDocument>(kitchen, $"/api/print/kot/{newTicket.Id}");
        (addition.IsAddition, addition.SeatGroup).Should().Be((true, "KAA+B"));
        addition.Items.Select(i => (i.Name, i.Quantity)).Should().Equal(new[] { (second.Name, 2) }, "the earlier dishes are not printed again");
        added.Tickets.Select(t => t.Id).Distinct().Count().Should().Be(3, "no ticket is duplicated");
        await File.WriteAllBytesAsync(Path.Combine(pdfFolder, $"kot-{newTicket.TicketNumber}-addition.pdf"), await kitchen.GetByteArrayAsync($"/api/print/kot/{newTicket.Id}.pdf"));
    }

    [Fact]
    public async Task TicketsFromBeforeSeatGroups_StayAccessible_KeepTheirStatus_AndMixWithNewOnes()
    {
        var waiter = await _factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword);
        var kitchen = await _factory.CreateAuthenticatedClientAsync("kitchen1", ApiFactory.DemoPassword);
        var table = await NewTableAsync("OLD", 4);
        var (first, second) = await TwoItemsAsync(waiter);
        var order = await PostJson<OrderDetailDto>(waiter, "/api/orders", new CreateOrderRequest
        {
            TableId = table.Id, GuestCount = 3, Submit = true,
            Items = new[]
            {
                new OrderItemInput { MenuItemId = first.Id, SeatCode = "OLDA+B" },
                new OrderItemInput { MenuItemId = second.Id, SeatCode = "OLDC" },
            },
        });

        // an order sent before this version had one shared ticket without a seat group: make this one look like that
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.KitchenOrders.Where(k => k.OrderId == order.Id).ExecuteUpdateAsync(s => s.SetProperty(k => k.SeatCode, (string?)null));
        }

        var oldTicket = order.Tickets.First();
        (await kitchen.PostAsync($"/api/kitchen/orders/{oldTicket.Id}/start", null)).EnsureSuccessStatusCode();
        var list = (await GetJsonAsync<KitchenTicketListDto>(kitchen, "/api/kitchen/orders")).Tickets.Where(t => t.OrderId == order.Id).ToList();
        list.Should().HaveCount(2, "old tickets are still listed");
        list.Should().OnlyContain(t => t.SeatCode == null);
        list.Single(t => t.Id == oldTicket.Id).Status.Should().Be(KitchenOrderStatus.Preparing, "the status was kept and can still be changed");
        (await GetJsonAsync<KitchenTicketDocument>(kitchen, $"/api/print/kot/{oldTicket.Id}")).SeatGroup.Should().BeNull();

        // the order still shows each seat group's status from its own dishes' tickets
        var detail = await GetJsonAsync<OrderDetailDto>(waiter, $"/api/orders/{order.Id}");
        detail.SeatGroups.Select(g => g.SeatCode).Should().Equal("OLDA+B", "OLDC");

        // a dish added now gets a ticket for its seat group beside the old ones
        var added = await PostJson<OrderDetailDto>(waiter, $"/api/orders/{order.Id}/items", new AppendOrderItemsRequest
        {
            Items = new[] { new OrderItemInput { MenuItemId = second.Id, SeatCode = "OLDD" } },
        });
        added.Tickets.Should().HaveCount(3);
        added.Tickets.Where(t => t.SeatCode is not null).Select(t => t.SeatCode).Should().Equal("OLDD");
    }

    // ---- live updates -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task SeatGroupOrders_ReachEveryRoleLive_AndAClientThatWasOffline_CatchesUpAfterReconnecting()
    {
        var table = await NewTableAsync("LV", 4);
        var waiter = await _factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword);
        var kitchen = await _factory.CreateAuthenticatedClientAsync("kitchen1", ApiFactory.DemoPassword);
        var item = await ItemAsync(waiter);

        // a screen for every role: waiter, kitchen display, cashier counter, manager
        var seen = new Dictionary<string, ConcurrentQueue<string>>();
        string Seen() => string.Join(" || ", seen.Select(kv => kv.Key + " = " + string.Join(", ", kv.Value)));
        async Task<HubConnection> ScreenAsync(string user, string device)
        {
            var login = await _factory.LoginAsync(user, ApiFactory.DemoPassword, deviceName: device);
            var hub = _factory.CreateHubConnection(login.AccessToken);
            var queue = seen[device] = new ConcurrentQueue<string>();
            hub.On<KitchenTicketCreatedEvent>(HubEvents.KitchenTicketCreated, e => queue.Enqueue($"created:{e.TicketId}"));
            hub.On<KitchenTicketUpdatedEvent>(HubEvents.KitchenTicketUpdated, e => queue.Enqueue($"ticket:{e.TicketId}:{e.Status}"));
            hub.On<OrderUpdatedEvent>(HubEvents.OrderUpdated, e => queue.Enqueue($"order:{e.OrderId}:{e.ChangeType}"));
            hub.On<OrderCreatedEvent>(HubEvents.OrderCreated, e => queue.Enqueue($"ordercreated:{e.OrderId}"));
            hub.On<OrderProgressEvent>(HubEvents.OrderPreparing, e => queue.Enqueue($"preparing:{e.OrderId}"));
            hub.On<OrderProgressEvent>(HubEvents.OrderReady, e => queue.Enqueue($"ready:{e.OrderId}"));
            await hub.StartAsync();
            await hub.InvokeAsync<DateTime>(HubMethods.Ping);
            return hub;
        }

        await using var waiterScreen = await ScreenAsync("waiter1", "LIVE-WAITER");
        await using var kitchenScreen = await ScreenAsync("kitchen1", "LIVE-KDS");
        await using var cashierScreen = await ScreenAsync("cashier1", "LIVE-COUNTER");
        await using var managerScreen = await ScreenAsync("manager1", "LIVE-MANAGER");

        var order = await PostJson<OrderDetailDto>(waiter, "/api/orders", new CreateOrderRequest
        {
            TableId = table.Id, GuestCount = 4, Submit = true,
            Items = new[] { new OrderItemInput { MenuItemId = item.Id, SeatCode = "LVA+B" }, new OrderItemInput { MenuItemId = item.Id, SeatCode = "LVC" } },
        });
        order.Tickets.Should().HaveCount(2);

        // the kitchen display gets one ticket per seat group; the counter and the manager hear about the order (the waiter who sent it has the answer to its own request)
        await WaitUntilAsync(() => seen["LIVE-KDS"].Count(e => e.StartsWith("created:", StringComparison.Ordinal)) == 2);
        seen["LIVE-KDS"].Where(e => e.StartsWith("created:", StringComparison.Ordinal)).Distinct().Should().HaveCount(2, "two seat groups, two tickets, each announced once");
        try
        {
            await WaitUntilAsync(() => new[] { "LIVE-COUNTER", "LIVE-MANAGER" }.All(d => seen[d].Contains($"ordercreated:{order.Id}")));
        }
        catch (TimeoutException)
        {
            throw new TimeoutException("Screens saw: " + string.Join(" || ", seen.Select(kv => kv.Key + " = " + string.Join(", ", kv.Value))) + " (order " + order.Id + ")");
        }

        // the kitchen starts only seat group A+B: every screen hears it
        var ab = order.Tickets.Single(t => t.SeatCode == "LVA+B");
        (await kitchen.PostAsync($"/api/kitchen/orders/{ab.Id}/start", null)).EnsureSuccessStatusCode();
        // waiter, manager and kitchen get the ticket event; the counter gets the order-level progress event, and then reads the order
        await WaitUntilAsync(() => new[] { "LIVE-WAITER", "LIVE-MANAGER", "LIVE-KDS" }.All(d => seen[d].Contains($"ticket:{ab.Id}:{KitchenOrderStatus.Preparing}"))
            && seen["LIVE-COUNTER"].Contains($"preparing:{order.Id}"), Seen);
        var counterView = await GetJsonAsync<OrderDetailDto>(await _factory.CreateAuthenticatedClientAsync("cashier1", ApiFactory.DemoPassword), $"/api/orders/{order.Id}");
        counterView.SeatGroups.Single(g => g.SeatCode == "LVA+B").KitchenStatus.Should().Be(KitchenOrderStatus.Preparing);
        counterView.SeatGroups.Single(g => g.SeatCode == "LVC").KitchenStatus.Should().Be(KitchenOrderStatus.New, "group C is not started: it does not inherit A+B's status");

        // the manager's screen loses its connection; meanwhile the kitchen finishes group A+B and starts C
        await managerScreen.StopAsync();
        var c = order.Tickets.Single(t => t.SeatCode == "LVC");
        (await kitchen.PostAsync($"/api/kitchen/orders/{ab.Id}/ready", null)).EnsureSuccessStatusCode();
        (await kitchen.PostAsync($"/api/kitchen/orders/{c.Id}/start", null)).EnsureSuccessStatusCode();
        seen["LIVE-MANAGER"].Should().NotContain(e => e == $"ticket:{ab.Id}:{KitchenOrderStatus.Ready}", "an offline screen hears nothing");

        // on reconnecting it reloads from the server (the same read every screen does) and sees the true state of each group
        await managerScreen.StartAsync();
        await managerScreen.InvokeAsync<DateTime>(HubMethods.Ping);
        var reconciled = await GetJsonAsync<OrderDetailDto>(await _factory.CreateAuthenticatedClientAsync("manager1", ApiFactory.DemoPassword), $"/api/orders/{order.Id}");
        reconciled.SeatGroups.Single(g => g.SeatCode == "LVA+B").KitchenStatus.Should().Be(KitchenOrderStatus.Ready);
        reconciled.SeatGroups.Single(g => g.SeatCode == "LVC").KitchenStatus.Should().Be(KitchenOrderStatus.Preparing);

        // and it is live again for what happens next
        (await kitchen.PostAsync($"/api/kitchen/orders/{c.Id}/ready", null)).EnsureSuccessStatusCode();
        try
        {
            await WaitUntilAsync(() => seen["LIVE-MANAGER"].Any(e => e == $"ticket:{c.Id}:{KitchenOrderStatus.Ready}"));
        }
        catch (TimeoutException)
        {
            throw new TimeoutException("The reconnected manager screen saw: " + string.Join(", ", seen["LIVE-MANAGER"]));
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, Func<string>? seen = null)
    {
        var until = DateTime.UtcNow.AddSeconds(15);
        while (!condition())
        {
            if (DateTime.UtcNow > until)
            {
                throw new TimeoutException("The live update did not arrive in 15 seconds." + (seen is null ? string.Empty : " Screens saw: " + seen()));
            }

            await Task.Delay(50);
        }
    }

    // ---- helpers ------------------------------------------------------------------------------------------------------

    private async Task<BillDetailDto> ServeAndBillAsync(HttpClient waiter, HttpClient kitchen, OrderDetailDto order)
    {
        foreach (var ticket in order.Tickets)
        {
            (await kitchen.PostAsync($"/api/kitchen/orders/{ticket.Id}/start", null)).EnsureSuccessStatusCode();
            (await kitchen.PostAsync($"/api/kitchen/orders/{ticket.Id}/ready", null)).EnsureSuccessStatusCode();
        }

        await PostJson<OrderDetailDto>(waiter, $"/api/orders/{order.Id}/serve", new { });
        return await PostJson<BillDetailDto>(waiter, $"/api/orders/{order.Id}/request-bill", new { });
    }

    private static async Task OccupyAndMergeAsync(HttpClient waiter, TableDto primary, TableDto companion, int guests)
    {
        await PostJson<TableDto>(waiter, $"/api/tables/{primary.Id}/occupy", new OccupyTableRequest { GuestCount = guests, RowVersion = primary.RowVersion });
        (await Post(waiter, $"/api/tables/{primary.Id}/merge", new MergeTablesRequest { CompanionTableId = companion.Id })).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<TableDto> NewTableAsync(string code, int capacity)
    {
        var manager = await _factory.CreateAuthenticatedClientAsync("manager1", ApiFactory.DemoPassword);
        var section = (await GetJsonAsync<List<SectionDto>>(manager, "/api/sections")).First(s => s.IsActive);
        (await manager.PostAsJsonAsync("/api/tables", new CreateTableRequest { Code = code, SectionId = section.Id, Capacity = capacity }, PosJson.Options))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        return await LookupAsync(manager, code);
    }

    private static async Task<TableDto> LookupAsync(HttpClient client, string code) =>
        (await GetJsonAsync<TableMapDto>(client, "/api/tables")).Sections.SelectMany(s => s.Tables).Single(t => t.Code == code);

    private static async Task<MenuEntryDto> ItemAsync(HttpClient client) => (await TwoItemsAsync(client)).First;

    // Two different dishes with no options, no stock count and a price, so tickets and bills can tell them apart.
    private static async Task<(MenuEntryDto First, MenuEntryDto Second)> TwoItemsAsync(HttpClient client)
    {
        var items = (await GetJsonAsync<MenuDto>(client, "/api/menu")).Items
            .Where(i => i.IsAvailable && i.ModifierGroupIds.Count == 0 && i.Price > 0 && i.StockMode == StockTrackingMode.None && i.Name != "Butter Naan")
            .OrderBy(i => i.Id).ToList();
        return (items[0], items[1]);
    }

    private static async Task<T> GetJsonAsync<T>(HttpClient client, string url)
    {
        var envelope = await (await client.GetAsync(url)).ReadEnvelopeAsync<T>();
        envelope.Success.Should().BeTrue($"{url}: {envelope.Message}");
        return envelope.Data!;
    }

    private static Task<HttpResponseMessage> PayAsync(HttpClient client, BillDetailDto bill, int method, decimal amount, string? reference = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/billing/{bill.Id}/payments")
        {
            Content = JsonContent.Create(new AddPaymentRequest { PaymentMethodId = method, Amount = amount, Reference = reference, RowVersion = bill.RowVersion }, options: PosJson.Options),
        };
        request.Headers.Add(PosHeaders.IdempotencyKey, Guid.NewGuid().ToString());
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> Put(HttpClient client, string url, object body) =>
        client.PutAsJsonAsync(url, body, PosJson.Options);

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
