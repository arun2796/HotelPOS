using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Domain.Common;
using HotelPOS.Domain.Floor;
using HotelPOS.Domain.Orders;

namespace HotelPOS.Domain.Tests.Orders;

public class OrderStateMachineTests
{
    private static readonly HashSet<(OrderStatus From, OrderStatus To)> Allowed = BuildAllowed();

    public static TheoryData<OrderStatus, OrderStatus> EveryPair()
    {
        var data = new TheoryData<OrderStatus, OrderStatus>();
        foreach (var from in Enum.GetValues<OrderStatus>())
        {
            foreach (var to in Enum.GetValues<OrderStatus>())
            {
                data.Add(from, to);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryPair))]
    public void CanTransition_MatchesTheDocumentedStateMachine(OrderStatus from, OrderStatus to)
    {
        OrderStateMachine.CanTransition(from, to).Should().Be(Allowed.Contains((from, to)), $"{from} -> {to}");
    }

    [Fact]
    public void PaidOrders_NeverGoBackToTheKitchen()
    {
        OrderStateMachine.CanTransition(OrderStatus.Paid, OrderStatus.Preparing).Should().BeFalse();
        OrderStateMachine.CanTransition(OrderStatus.Completed, OrderStatus.Served).Should().BeFalse();
    }

    [Theory]
    [InlineData(OrderStatus.Draft, false, true, CancelPermission.Allowed)]
    [InlineData(OrderStatus.Submitted, false, false, CancelPermission.NotOwner)]
    [InlineData(OrderStatus.Accepted, true, false, CancelPermission.Allowed)]
    [InlineData(OrderStatus.Preparing, false, true, CancelPermission.ManagerOnly)]
    [InlineData(OrderStatus.Ready, true, false, CancelPermission.AllowedWithReason)]
    [InlineData(OrderStatus.Served, true, true, CancelPermission.NotCancellable)]
    [InlineData(OrderStatus.Paid, true, true, CancelPermission.NotCancellable)]
    public void CanCancel_FollowsTheRoleRules(OrderStatus status, bool isManager, bool isOwner, CancelPermission expected)
    {
        OrderStateMachine.CanCancel(status, isManager, isOwner).Should().Be(expected);
    }

    private static HashSet<(OrderStatus, OrderStatus)> BuildAllowed()
    {
        var band = new[] { OrderStatus.Submitted, OrderStatus.Accepted, OrderStatus.Preparing, OrderStatus.Ready, OrderStatus.Served };
        var allowed = new HashSet<(OrderStatus, OrderStatus)>
        {
            (OrderStatus.Draft, OrderStatus.Submitted),
            (OrderStatus.Draft, OrderStatus.Cancelled),
            (OrderStatus.Submitted, OrderStatus.Cancelled),
            (OrderStatus.Accepted, OrderStatus.Cancelled),
            (OrderStatus.Preparing, OrderStatus.Cancelled),
            (OrderStatus.Ready, OrderStatus.Cancelled),
            (OrderStatus.Ready, OrderStatus.BillRequested),
            (OrderStatus.Served, OrderStatus.BillRequested),
            (OrderStatus.BillRequested, OrderStatus.Billed),
            (OrderStatus.BillRequested, OrderStatus.Served),
            (OrderStatus.Billed, OrderStatus.Paid),
            (OrderStatus.Billed, OrderStatus.Served),
            (OrderStatus.Billed, OrderStatus.Cancelled),
            (OrderStatus.Paid, OrderStatus.Completed),
        };
        foreach (var from in band)
        {
            foreach (var to in band.Where(t => t != from))
            {
                allowed.Add((from, to));
            }
        }

        return allowed;
    }
}

public class OrderTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    private static Table NewTable() => new("T05", null, 1, 4) { Id = 5 };

    private static OrderItem Item(string name = "Naan", decimal price = 50m, int quantity = 1) =>
        new(1, name, price, quantity, null, null, 5m, 1, new[] { new OrderItemModifier(9, "Butter", 10m) });

    [Fact]
    public void NewOrder_IsADraft_AndRemembersWhetherGuestsWereAlreadySeated()
    {
        var available = new Order(NewTable(), waiterId: 3, guestCount: 2, notes: " window ");
        var occupiedTable = NewTable();
        occupiedTable.Occupy(4, Now);
        var occupied = new Order(occupiedTable, 3, 4, null);

        available.Status.Should().Be(OrderStatus.Draft);
        available.Notes.Should().Be("window");
        available.OpenedOnOccupiedTable.Should().BeFalse();
        occupied.OpenedOnOccupiedTable.Should().BeTrue();
    }

    [Fact]
    public void Submit_SendsTheDraftItems_AndNeedsAtLeastOne()
    {
        var empty = new Order(NewTable(), 3, 2, null);
        empty.Invoking(o => o.Submit(Now)).Should().Throw<DomainException>();

        var order = new Order(NewTable(), 3, 2, null);
        order.ReplaceDraftItems(new[] { Item(), Item("Lassi") });
        order.Submit(Now);

        order.Status.Should().Be(OrderStatus.Submitted);
        order.SubmittedAt.Should().Be(Now);
        order.Items.Should().OnlyContain(i => i.Status == OrderItemStatus.Sent && i.BatchNumber == 1);
        order.Invoking(o => o.Submit(Now)).Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.InvalidStateTransition);
    }

    [Fact]
    public void AppendBatch_NumbersBatches_AndIsRefusedForDraftsAndBilledOrders()
    {
        var order = new Order(NewTable(), 3, 2, null);
        order.ReplaceDraftItems(new[] { Item() });
        order.Invoking(o => o.AppendBatch(new[] { Item() })).Should().Throw<DomainException>();
        order.Submit(Now);

        order.AppendBatch(new[] { Item("Coke") }).Should().Be(2);
        order.AppendBatch(new[] { Item("Kulfi"), Item("Lassi") }).Should().Be(3);

        order.Items.Select(i => i.BatchNumber).Should().Equal(1, 2, 3, 3);
    }

    [Fact]
    public void LineTotals_IncludeModifiers_AndCancelledItemsAreLeftOut()
    {
        var order = new Order(NewTable(), 3, 2, null);
        order.ReplaceDraftItems(new[] { Item(price: 50m, quantity: 2) });

        order.ApproxSubtotal.Should().Be(120m);
        order.Cancel(3, "Guests left", Now);
        order.ApproxSubtotal.Should().Be(0m);
        order.Status.Should().Be(OrderStatus.Cancelled);
    }

    [Fact]
    public void Table_FollowsTheOrder_AndCancellingRestoresWhatWasThereBefore()
    {
        var table = NewTable();
        var order = new Order(table, 3, 2, null);
        table.AttachOrder(order, Now);

        table.Status.Should().Be(TableStatus.Ordering);
        table.GuestCount.Should().Be(2);
        table.CanTakeNewOrder.Should().BeFalse();
        table.Invoking(t => t.AttachOrder(new Order(t, 4, 2, null), Now)).Should().Throw<DomainException>()
            .Which.Code.Should().Be(ErrorCodes.TableNotAvailable);

        table.FollowOrder(OrderStatus.Submitted);
        table.Status.Should().Be(TableStatus.Preparing);

        table.DetachOrder(keepGuestsSeated: false);
        table.Status.Should().Be(TableStatus.Available);
        table.GuestCount.Should().BeNull();

        table.Occupy(3, Now);
        table.AttachOrder(new Order(table, 3, 3, null), Now);
        table.DetachOrder(keepGuestsSeated: true);
        table.Status.Should().Be(TableStatus.Occupied);
        table.GuestCount.Should().Be(3);
    }

    [Fact]
    public void OutOfServiceTable_CannotTakeAnOrder()
    {
        var table = NewTable();
        table.SetOutOfService();

        table.CanTakeNewOrder.Should().BeFalse();
    }
}
