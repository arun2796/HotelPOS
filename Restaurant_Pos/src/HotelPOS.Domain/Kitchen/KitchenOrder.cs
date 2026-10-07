using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Domain.Common;
using HotelPOS.Domain.Orders;

namespace HotelPOS.Domain.Kitchen;

public static class KitchenOrderStateMachine
{
    public static bool IsOpen(KitchenOrderStatus status) => status is not (KitchenOrderStatus.Completed or KitchenOrderStatus.Cancelled);

    public static bool CanTransition(KitchenOrderStatus from, KitchenOrderStatus to) => (from, to) switch
    {
        (KitchenOrderStatus.New, KitchenOrderStatus.Accepted or KitchenOrderStatus.Preparing) => true,
        (KitchenOrderStatus.Accepted, KitchenOrderStatus.Preparing) => true,
        (KitchenOrderStatus.Preparing, KitchenOrderStatus.Ready) => true,
        (KitchenOrderStatus.Ready, KitchenOrderStatus.Completed or KitchenOrderStatus.Preparing) => true,
        (KitchenOrderStatus.New or KitchenOrderStatus.Accepted or KitchenOrderStatus.Preparing or KitchenOrderStatus.Ready, KitchenOrderStatus.Cancelled) => true,
        _ => false,
    };
}

public sealed class KitchenOrder : BaseEntity, IHasRowVersion
{
    private readonly List<KitchenOrderItem> _items = new();

    private KitchenOrder()
    {
    }

    public KitchenOrder(Order order, int batchNumber, int stationId, string ticketNumber, IEnumerable<OrderItem> items)
    {
        OrderId = order.Id;
        Order = order;
        BatchNumber = batchNumber;
        PreparationStationId = stationId;
        TicketNumber = ticketNumber;
        Status = KitchenOrderStatus.New;
        _items.AddRange(items.Select(i => new KitchenOrderItem(this, i)));
        if (_items.Count == 0)
        {
            throw new DomainException("A kitchen ticket needs at least one item.");
        }
    }

    public int OrderId { get; private set; }
    public Order? Order { get; private set; }
    public string TicketNumber { get; private set; } = string.Empty;
    public int BatchNumber { get; private set; }
    public int PreparationStationId { get; private set; }
    public KitchenOrderStatus Status { get; private set; }
    public DateTime? AcceptedAt { get; private set; }
    public int? AcceptedBy { get; private set; }
    public DateTime? StartedAt { get; private set; }
    public DateTime? ReadyAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public int? CompletedBy { get; private set; }
    public DateTime? CancelledAt { get; private set; }
    public uint RowVersion { get; set; }

    public IReadOnlyCollection<KitchenOrderItem> Items => _items;

    public bool IsOpen => KitchenOrderStateMachine.IsOpen(Status);

    public void Accept(int userId, DateTime nowUtc)
    {
        MoveTo(KitchenOrderStatus.Accepted);
        AcceptedAt = nowUtc;
        AcceptedBy = userId;
    }

    public void Start(int userId, DateTime nowUtc)
    {
        if (Status == KitchenOrderStatus.New)
        {
            AcceptedAt = nowUtc;
            AcceptedBy = userId;
        }

        MoveTo(KitchenOrderStatus.Preparing);
        StartedAt ??= nowUtc;
    }

    public void MarkReady(DateTime nowUtc)
    {
        MoveTo(KitchenOrderStatus.Ready);
        ReadyAt = nowUtc;
    }

    public void Recall()
    {
        MoveTo(KitchenOrderStatus.Preparing);
        ReadyAt = null;
    }

    public void Complete(int userId, DateTime nowUtc)
    {
        MoveTo(KitchenOrderStatus.Completed);
        CompletedAt = nowUtc;
        CompletedBy = userId;
    }

    public void Cancel(DateTime nowUtc)
    {
        if (!IsOpen)
        {
            return;
        }

        MoveTo(KitchenOrderStatus.Cancelled);
        CancelledAt = nowUtc;
        foreach (var item in _items)
        {
            item.MarkCancelled();
        }
    }

    // Returns true when this was the last open item, which cancels the whole ticket.
    public bool CancelItem(int orderItemId, DateTime nowUtc)
    {
        var item = _items.FirstOrDefault(i => i.OrderItemId == orderItemId)
            ?? throw new DomainException("The item is not on this ticket.");
        item.MarkCancelled();
        if (_items.All(i => i.IsCancelled))
        {
            Cancel(nowUtc);
            return true;
        }

        return false;
    }

    private void MoveTo(KitchenOrderStatus target)
    {
        if (!KitchenOrderStateMachine.CanTransition(Status, target))
        {
            throw new DomainException($"Ticket {TicketNumber} is {Status} and cannot become {target}.", ErrorCodes.InvalidStateTransition);
        }

        Status = target;
    }
}

public sealed class KitchenOrderItem
{
    private KitchenOrderItem()
    {
    }

    internal KitchenOrderItem(KitchenOrder ticket, OrderItem item)
    {
        KitchenOrder = ticket;
        OrderItem = item;
        OrderItemId = item.Id;
        Quantity = item.Quantity;
    }

    public int Id { get; private set; }
    public int KitchenOrderId { get; private set; }
    public KitchenOrder? KitchenOrder { get; private set; }
    public int OrderItemId { get; private set; }
    public OrderItem? OrderItem { get; private set; }
    public int Quantity { get; private set; }
    public bool IsCancelled { get; private set; }

    internal void MarkCancelled() => IsCancelled = true;
}

public static class OrderStatusDeriver
{
    // docs/02 § 4.1. Null when no ticket is left open or completed (everything cancelled): the status stays as it is.
    public static OrderStatus? Derive(IEnumerable<KitchenOrderStatus> ticketStatuses)
    {
        var live = ticketStatuses.Where(s => s != KitchenOrderStatus.Cancelled).ToList();
        if (live.Count == 0)
        {
            return null;
        }

        if (live.All(s => s == KitchenOrderStatus.Completed))
        {
            return OrderStatus.Served;
        }

        var open = live.Where(s => s != KitchenOrderStatus.Completed).ToList();
        if (open.All(s => s == KitchenOrderStatus.Ready))
        {
            return OrderStatus.Ready;
        }

        if (open.Contains(KitchenOrderStatus.Preparing))
        {
            return OrderStatus.Preparing;
        }

        return open.Contains(KitchenOrderStatus.Accepted) ? OrderStatus.Accepted : OrderStatus.Submitted;
    }
}
