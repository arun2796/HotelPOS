using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Features.Floor.Commands;
using HotelPOS.Application.Features.Kitchen.Commands;
using HotelPOS.Application.Features.Kitchen.Queries;
using HotelPOS.Application.Features.Menu.Commands;
using HotelPOS.Application.Features.Menu.Queries;
using HotelPOS.Application.Features.Orders.Commands;
using HotelPOS.Application.Features.Orders.Queries;
using HotelPOS.Application.Tests.Support;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Kitchen;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Orders;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HotelPOS.Application.Tests.Kitchen;

public class KitchenTests : DatabaseTestBase
{
    public KitchenTests(DatabaseFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task Submit_WithItemsForTwoStations_CreatesOneTicketPerStation()
    {
        var s = await SetupAsync();
        var order = await SendAsync(s, Line(s.Biryani, 2), Line(s.Soda, 3));

        var tickets = (await SendAsAdminAsync(new ListKitchenTicketsQuery(new KitchenTicketQuery()))).Value;
        var mine = tickets.Tickets.Where(t => t.OrderId == order.Id).OrderBy(t => t.StationCode).ToList();

        mine.Select(t => t.TicketNumber).Should().Equal($"{order.OrderNumber}-1-BAR", $"{order.OrderNumber}-1-MAIN");
        mine[0].Items.Should().ContainSingle().Which.Should().Match<KitchenTicketItemDto>(i => i.Name == "Lime Soda" && i.Quantity == 3);
        mine[1].Items.Should().ContainSingle().Which.Name.Should().Be("Chicken Biryani");
        mine.Should().OnlyContain(t => t.Status == KitchenOrderStatus.New && t.TableCode == "T05");
        Realtime.Events.Count(e => e.EventName == HubEvents.KitchenTicketCreated).Should().Be(2);
        (await SendAsAdminAsync(new ListKitchenTicketsQuery(new KitchenTicketQuery { StationId = s.Bar }))).Value
            .Tickets.Should().OnlyContain(t => t.StationCode == "BAR");
    }

    [Fact]
    public async Task Workflow_RecordsTimestamps_AndDrivesOrderAndTableStatus()
    {
        var s = await SetupAsync();
        var order = await SendAsync(s, Line(s.Biryani, 1));
        var ticketId = await TicketIdAsync(order.Id, "MAIN");
        AsKitchen(s);

        await SendAsync(new AcceptKitchenTicketCommand(ticketId));
        Clock.Advance(TimeSpan.FromMinutes(2));
        await SendAsync(new StartKitchenTicketCommand(ticketId));
        (await OrderStatusAsync(order.Id)).Should().Be(OrderStatus.Preparing);
        Clock.Advance(TimeSpan.FromMinutes(10));
        var ready = await SendAsync(new MarkKitchenTicketReadyCommand(ticketId));

        ready.Value.AcceptedAtUtc.Should().Be(TestClock.Start);
        ready.Value.StartedAtUtc.Should().Be(TestClock.Start.AddMinutes(2));
        ready.Value.ReadyAtUtc.Should().Be(TestClock.Start.AddMinutes(12));
        (await OrderStatusAsync(order.Id)).Should().Be(OrderStatus.Ready);
        (await TableStatusAsync(s.TableId)).Should().Be(TableStatus.Ready);
        var readyEvent = Realtime.Events.Single(e => e.EventName == HubEvents.OrderReady);
        readyEvent.Audience.UserIds.Should().Contain(s.WaiterA);
        ((OrderProgressEvent)readyEvent.Payload).ReadyTicketNumbers.Should().Equal($"{order.OrderNumber}-1");

        var recalled = await SendAsync(new RecallKitchenTicketCommand(ticketId));
        recalled.Value.Status.Should().Be(KitchenOrderStatus.Preparing);
        recalled.Value.ReadyAtUtc.Should().BeNull();
        (await TableStatusAsync(s.TableId)).Should().Be(TableStatus.Preparing);
    }

    [Fact]
    public async Task Serve_CompletesOnlyReadyTickets_AndTheOrderIsServedWhenAllAreDone()
    {
        var s = await SetupAsync();
        var order = await SendAsync(s, Line(s.Biryani, 1), Line(s.Soda, 1));
        var bar = await TicketIdAsync(order.Id, "BAR");
        var main = await TicketIdAsync(order.Id, "MAIN");
        AsKitchen(s);
        await SendAsync(new StartKitchenTicketCommand(bar));
        await SendAsync(new MarkKitchenTicketReadyCommand(bar));
        (await OrderStatusAsync(order.Id)).Should().Be(OrderStatus.Submitted);
        (await TableStatusAsync(s.TableId)).Should().Be(TableStatus.Ready);

        AsWaiter(s.WaiterA);
        var firstServe = await SendAsync(new ServeOrderCommand(order.Id));
        firstServe.Value.Tickets.Single(t => t.Id == bar).Status.Should().Be(KitchenOrderStatus.Completed);
        firstServe.Value.Tickets.Single(t => t.Id == main).Status.Should().Be(KitchenOrderStatus.New);
        var nothingReady = await SendAsync(new ServeOrderCommand(order.Id));
        nothingReady.Error!.Code.Should().Be(ErrorCodes.InvalidStateTransition);

        AsKitchen(s);
        await SendAsync(new StartKitchenTicketCommand(main));
        await SendAsync(new MarkKitchenTicketReadyCommand(main));
        AsWaiter(s.WaiterB);
        var served = await SendAsync(new ServeOrderCommand(order.Id));

        served.Value.Status.Should().Be(OrderStatus.Served);
        (await TableStatusAsync(s.TableId)).Should().Be(TableStatus.Occupied);
        Realtime.Events.Should().Contain(e => e.EventName == HubEvents.OrderServed);
    }

    [Fact]
    public async Task Append_CreatesABatchTwoTicket_AndReopensAServedOrder()
    {
        var s = await SetupAsync();
        var order = await SendAsync(s, Line(s.Soda, 1));
        var bar = await TicketIdAsync(order.Id, "BAR");
        AsKitchen(s);
        await SendAsync(new StartKitchenTicketCommand(bar));
        await SendAsync(new MarkKitchenTicketReadyCommand(bar));
        await SendAsync(new CompleteKitchenTicketCommand(bar));
        (await OrderStatusAsync(order.Id)).Should().Be(OrderStatus.Served);

        AsWaiter(s.WaiterA);
        var appended = await SendAsync(new AppendOrderItemsCommand(order.Id, new AppendOrderItemsRequest { Items = new[] { Line(s.Kulfi, 2) } }));

        var ticket = appended.Value.Tickets.Single(t => t.BatchNumber == 2);
        ticket.TicketNumber.Should().Be($"{order.OrderNumber}-2");
        ticket.Status.Should().Be(KitchenOrderStatus.New);
        appended.Value.Status.Should().Be(OrderStatus.Submitted);
        (await TableStatusAsync(s.TableId)).Should().Be(TableStatus.Preparing);
    }

    [Fact]
    public async Task TicketItems_CarryTheMenuItemPictureUrl_AndNullWhenTheItemHasNone()
    {
        var s = await SetupAsync();
        await QueryAsync(async db =>
        {
            (await db.MenuItems.SingleAsync(m => m.Id == s.Biryani)).SetImage("biryani.jpg");
            return await db.SaveChangesAsync();
        });
        var order = await SendAsync(s, Line(s.Biryani, 1), Line(s.Kulfi, 1));

        var list = await SendAsAdminAsync(new ListKitchenTicketsQuery(new KitchenTicketQuery()));

        var items = list.Value.Tickets.Single(t => t.OrderId == order.Id).Items;
        items.Single(i => i.Name == "Chicken Biryani").ImageUrl.Should().Be(MenuImageRoutes.UrlFor("biryani.jpg"));
        items.Single(i => i.Name == "Kulfi").ImageUrl.Should().BeNull();
        var ticketId = list.Value.Tickets.Single(t => t.OrderId == order.Id).Id;
        var detail = await SendAsAdminAsync(new GetKitchenTicketQuery(ticketId));
        detail.Value.Items.Single(i => i.Name == "Chicken Biryani").ImageUrl.Should().Be("/images/menu/biryani.jpg");
    }

    [Fact]
    public async Task CancellingAnOrder_CancelsItsOpenTickets()
    {
        var s = await SetupAsync();
        var order = await SendAsync(s, Line(s.Biryani, 1), Line(s.Soda, 1));

        var cancelled = await SendAsync(new CancelOrderCommand(order.Id, new CancelOrderRequest { Reason = "Guests left" }));

        cancelled.Value.Tickets.Should().HaveCount(2).And.OnlyContain(t => t.Status == KitchenOrderStatus.Cancelled);
        (await SendAsAdminAsync(new ListKitchenTicketsQuery(new KitchenTicketQuery()))).Value.Tickets.Should().BeEmpty();
        Realtime.Events.Count(e => e.EventName == HubEvents.KitchenTicketUpdated).Should().Be(2);
    }

    [Fact]
    public async Task ItemCancel_FollowsTheTicketRules_AndTheLastItemCancelsTheTicket()
    {
        var s = await SetupAsync();
        var order = await SendAsync(s, Line(s.Biryani, 1), Line(s.Kulfi, 1), Line(s.Soda, 1));
        var biryani = order.Items.Single(i => i.MenuItemId == s.Biryani).Id;
        var kulfi = order.Items.Single(i => i.MenuItemId == s.Kulfi).Id;
        var soda = order.Items.Single(i => i.MenuItemId == s.Soda).Id;
        var bar = await TicketIdAsync(order.Id, "BAR");
        var main = await TicketIdAsync(order.Id, "MAIN");

        AsWaiter(s.WaiterB);
        var notOwner = await SendAsync(new CancelOrderItemCommand(order.Id, kulfi, new CancelOrderItemRequest()));
        AsWaiter(s.WaiterA);
        var first = await SendAsync(new CancelOrderItemCommand(order.Id, kulfi, new CancelOrderItemRequest { Reason = "No dessert" }));

        AsKitchen(s);
        await SendAsync(new StartKitchenTicketCommand(main));
        AsWaiter(s.WaiterA);
        var preparingByWaiter = await SendAsync(new CancelOrderItemCommand(order.Id, biryani, new CancelOrderItemRequest()));
        CurrentUser.SignIn(s.Manager, "manager", Roles.Manager);
        var preparingByManager = await SendAsync(new CancelOrderItemCommand(order.Id, biryani, new CancelOrderItemRequest { Reason = "Allergy" }));

        AsKitchen(s);
        await SendAsync(new StartKitchenTicketCommand(bar));
        await SendAsync(new MarkKitchenTicketReadyCommand(bar));
        CurrentUser.SignIn(s.Manager, "manager", Roles.Manager);
        var ready = await SendAsync(new CancelOrderItemCommand(order.Id, soda, new CancelOrderItemRequest()));

        notOwner.Error!.Code.Should().Be(ErrorCodes.Forbidden);
        first.Value.Items.Single(i => i.Id == kulfi).Status.Should().Be(OrderItemStatus.Cancelled);
        first.Value.Tickets.Single(t => t.Id == main).Status.Should().Be(KitchenOrderStatus.New);
        preparingByWaiter.Error!.Code.Should().Be(ErrorCodes.Forbidden);
        preparingByManager.Value.Tickets.Single(t => t.Id == main).Status.Should().Be(KitchenOrderStatus.Cancelled);
        ready.Error!.Code.Should().Be(ErrorCodes.InvalidStateTransition);
        (await QueryAsync(db => db.AuditLogs.CountAsync(a => a.Action == "Order.ItemCancelled"))).Should().Be(2);
    }

    [Fact]
    public async Task CompletedList_ShowsTodaysFinishedAndCancelledTickets()
    {
        var s = await SetupAsync();
        var order = await SendAsync(s, Line(s.Biryani, 1), Line(s.Soda, 1));
        var bar = await TicketIdAsync(order.Id, "BAR");
        var main = await TicketIdAsync(order.Id, "MAIN");
        AsKitchen(s);
        await SendAsync(new StartKitchenTicketCommand(bar));
        await SendAsync(new MarkKitchenTicketReadyCommand(bar));
        await SendAsync(new CompleteKitchenTicketCommand(bar));
        AsWaiter(s.WaiterA);
        await SendAsync(new CancelOrderItemCommand(order.Id, order.Items.Single(i => i.MenuItemId == s.Biryani).Id, new CancelOrderItemRequest()));

        var done = (await SendAsAdminAsync(new ListCompletedKitchenTicketsQuery(null))).Value;

        done.Tickets.Select(t => (t.Id, t.Status)).Should().BeEquivalentTo(new[]
        {
            (bar, KitchenOrderStatus.Completed),
            (main, KitchenOrderStatus.Cancelled),
        });
    }

    private sealed record Setup(int TableId, int Main, int Bar, int Biryani, int Soda, int Kulfi, int WaiterA, int WaiterB, int Manager, int Cook);

    private async Task<Setup> SetupAsync()
    {
        CurrentUser.SignIn(await AdminIdAsync(), "admin", Roles.Admin);
        var section = await SendAsync(new CreateSectionCommand(new CreateSectionRequest { Name = "Ground Floor", SortOrder = 1 }));
        var table = await SendAsync(new CreateTableCommand(new CreateTableRequest { Code = "T05", SectionId = section.Value.Id, Capacity = 4 }));
        var category = await SendAsync(new CreateCategoryCommand(new CreateCategoryRequest { Name = "All day", SortOrder = 1 }));
        var main = await QueryAsync(db => db.PreparationStations.Where(p => p.Code == "MAIN").Select(p => p.Id).FirstAsync());
        var bar = await SendAsync(new CreateStationCommand(new SaveStationRequest { Name = "Bar", Code = "BAR", SortOrder = 2 }));

        async Task<int> ItemAsync(string name, int station)
        {
            var r = await SendAsync(new CreateMenuItemCommand(new CreateMenuItemRequest
            {
                CategoryId = category.Value.Id, Name = name, Price = 100m, PreparationStationId = station,
            }));
            r.IsSuccess.Should().BeTrue(r.Error?.Message);
            return r.Value.Id;
        }

        var setup = new Setup(
            table.Value.Id, main, bar.Value.Id,
            await ItemAsync("Chicken Biryani", main), await ItemAsync("Lime Soda", bar.Value.Id), await ItemAsync("Kulfi", main),
            (await CreateUserAsync("waitera", "secret1", Roles.Waiter)).Id,
            (await CreateUserAsync("waiterb", "secret1", Roles.Waiter)).Id,
            (await CreateUserAsync("managerx", "secret1", Roles.Manager)).Id,
            (await CreateUserAsync("cook", "secret1", Roles.Kitchen)).Id);
        AsWaiter(setup.WaiterA);
        Realtime.Reset();
        return setup;
    }

    private void AsWaiter(int id) => CurrentUser.SignIn(id, "waiter" + id, Roles.Waiter);

    private void AsKitchen(Setup s) => CurrentUser.SignIn(s.Cook, "cook", Roles.Kitchen);

    private static OrderItemInput Line(int menuItemId, int quantity) => new() { MenuItemId = menuItemId, Quantity = quantity };

    private async Task<OrderDetailDto> SendAsync(Setup s, params OrderItemInput[] items)
    {
        var result = await SendAsync(new CreateOrderCommand(new CreateOrderRequest
        {
            TableId = s.TableId,
            GuestCount = 2,
            Items = items,
            Submit = true,
        }));
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        return result.Value;
    }

    private async Task<int> TicketIdAsync(int orderId, string stationCode)
    {
        var order = await SendAsync(new GetOrderQuery(orderId));
        return order.Value.Tickets.Single(t => t.StationCode == stationCode).Id;
    }

    private Task<OrderStatus> OrderStatusAsync(int orderId) =>
        QueryAsync(db => db.Orders.Where(o => o.Id == orderId).Select(o => o.Status).SingleAsync());

    private Task<TableStatus> TableStatusAsync(int tableId) =>
        QueryAsync(db => db.Tables.Where(t => t.Id == tableId).Select(t => t.Status).SingleAsync());
}
