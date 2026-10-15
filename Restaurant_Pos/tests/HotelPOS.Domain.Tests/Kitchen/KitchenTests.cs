using HotelPOS.Contracts.Enums;
using HotelPOS.Domain.Common;
using HotelPOS.Domain.Floor;
using HotelPOS.Domain.Kitchen;
using HotelPOS.Domain.Orders;

namespace HotelPOS.Domain.Tests.Kitchen;

public class KitchenTicketTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    public static TheoryData<KitchenOrderStatus, KitchenOrderStatus, bool> Transitions()
    {
        var allowed = new HashSet<(KitchenOrderStatus, KitchenOrderStatus)>
        {
            (KitchenOrderStatus.New, KitchenOrderStatus.Accepted),
            (KitchenOrderStatus.New, KitchenOrderStatus.Preparing),
            (KitchenOrderStatus.Accepted, KitchenOrderStatus.Preparing),
            (KitchenOrderStatus.Preparing, KitchenOrderStatus.Ready),
            (KitchenOrderStatus.Ready, KitchenOrderStatus.Completed),
            (KitchenOrderStatus.Ready, KitchenOrderStatus.Preparing),
            (KitchenOrderStatus.New, KitchenOrderStatus.Cancelled),
            (KitchenOrderStatus.Accepted, KitchenOrderStatus.Cancelled),
            (KitchenOrderStatus.Preparing, KitchenOrderStatus.Cancelled),
            (KitchenOrderStatus.Ready, KitchenOrderStatus.Cancelled),
        };
        var data = new TheoryData<KitchenOrderStatus, KitchenOrderStatus, bool>();
        foreach (var from in Enum.GetValues<KitchenOrderStatus>())
        {
            foreach (var to in Enum.GetValues<KitchenOrderStatus>())
            {
                data.Add(from, to, allowed.Contains((from, to)));
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Transitions))]
    public void Transitions_FollowTheTicketStateMachine(KitchenOrderStatus from, KitchenOrderStatus to, bool allowed)
    {
        KitchenOrderStateMachine.CanTransition(from, to).Should().Be(allowed);
    }

    [Fact]
    public void Workflow_RecordsWhoAndWhen_AndStartFromNewAcceptsImplicitly()
    {
        var ticket = NewTicket(out _);

        ticket.Start(5, Now);
        ticket.MarkReady(Now.AddMinutes(9));
        ticket.Recall();
        ticket.MarkReady(Now.AddMinutes(11));
        ticket.Complete(6, Now.AddMinutes(12));

        ticket.AcceptedBy.Should().Be(5);
        ticket.AcceptedAt.Should().Be(Now);
        ticket.StartedAt.Should().Be(Now);
        ticket.ReadyAt.Should().Be(Now.AddMinutes(11));
        ticket.CompletedBy.Should().Be(6);
        ticket.Status.Should().Be(KitchenOrderStatus.Completed);
        ticket.Invoking(t => t.Recall()).Should().Throw<DomainException>();
    }

    [Fact]
    public void CancellingTheLastOpenItem_CancelsTheTicket()
    {
        var ticket = NewTicket(out var items);

        ticket.CancelItem(items[0].Id, Now).Should().BeFalse();
        ticket.Status.Should().Be(KitchenOrderStatus.New);
        ticket.CancelItem(items[1].Id, Now).Should().BeTrue();

        ticket.Status.Should().Be(KitchenOrderStatus.Cancelled);
        ticket.CancelledAt.Should().Be(Now);
    }

    private static KitchenOrder NewTicket(out List<OrderItem> items)
    {
        var table = new Table("T05", null, 1, 4) { Id = 5 };
        var order = new Order(table, 3, 2, null);
        items = new List<OrderItem>
        {
            new(1, "Biryani", 260m, 1, null, null, 5m, 1, Array.Empty<OrderItemModifier>()) { Id = 11 },
            new(2, "Naan", 50m, 2, null, null, 5m, 1, Array.Empty<OrderItemModifier>()) { Id = 12 },
        };
        order.ReplaceDraftItems(items);
        order.Submit(Now);
        return new KitchenOrder(order, 1, 1, "1001-1", items);
    }
}

public class OrderStatusDerivationTests
{
    [Theory]
    [InlineData(OrderStatus.Submitted, KitchenOrderStatus.New)]
    [InlineData(OrderStatus.Accepted, KitchenOrderStatus.Accepted, KitchenOrderStatus.New)]
    [InlineData(OrderStatus.Preparing, KitchenOrderStatus.Preparing, KitchenOrderStatus.Accepted, KitchenOrderStatus.New)]
    [InlineData(OrderStatus.Submitted, KitchenOrderStatus.Ready, KitchenOrderStatus.New)]
    [InlineData(OrderStatus.Preparing, KitchenOrderStatus.Ready, KitchenOrderStatus.Preparing)]
    [InlineData(OrderStatus.Ready, KitchenOrderStatus.Ready, KitchenOrderStatus.Completed)]
    [InlineData(OrderStatus.Served, KitchenOrderStatus.Completed, KitchenOrderStatus.Completed, KitchenOrderStatus.Cancelled)]
    [InlineData(OrderStatus.Submitted, KitchenOrderStatus.Completed, KitchenOrderStatus.New)]
    public void Derive_FollowsTheDocumentedRules(OrderStatus expected, params KitchenOrderStatus[] tickets)
    {
        OrderStatusDeriver.Derive(tickets).Should().Be(expected);
    }

    [Fact]
    public void Derive_LeavesTheStatusAlone_WhenEveryTicketWasCancelled()
    {
        OrderStatusDeriver.Derive(new[] { KitchenOrderStatus.Cancelled }).Should().BeNull();
    }

    [Fact]
    public void AddingABatchAfterServed_ReopensTheOrderForTheKitchen()
    {
        var table = new Table("T05", null, 1, 4) { Id = 5 };
        var order = new Order(table, 3, 2, null);
        order.ReplaceDraftItems(new[] { new OrderItem(1, "Biryani", 260m, 1, null, null, 5m, 1, Array.Empty<OrderItemModifier>()) });
        order.Submit(DateTime.UtcNow);
        order.ApplyKitchenStatus(OrderStatus.Served, DateTime.UtcNow).Should().BeTrue();
        order.ServedAt.Should().NotBeNull();

        var derived = OrderStatusDeriver.Derive(new[] { KitchenOrderStatus.Completed, KitchenOrderStatus.New });
        order.ApplyKitchenStatus(derived!.Value, DateTime.UtcNow);

        order.Status.Should().Be(OrderStatus.Submitted);
        order.ServedAt.Should().BeNull();
    }

    [Fact]
    public void Table_ShowsReady_WhileAnyTicketIsReady_AndOccupiedOnceServed()
    {
        var table = new Table("T05", null, 1, 4) { Id = 5 };
        table.AttachOrder(new Order(table, 3, 2, null), DateTime.UtcNow);

        table.FollowKitchen(OrderStatus.Submitted, anyTicketReady: true);
        table.Status.Should().Be(TableStatus.Ready);
        table.FollowKitchen(OrderStatus.Preparing, anyTicketReady: false);
        table.Status.Should().Be(TableStatus.Preparing);
        table.FollowKitchen(OrderStatus.Served, anyTicketReady: false);
        table.Status.Should().Be(TableStatus.Occupied);
    }
}
