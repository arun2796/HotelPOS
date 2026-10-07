using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Domain.Common;
using HotelPOS.Domain.Floor;

namespace HotelPOS.Domain.Orders;

public sealed class Order : BaseEntity, IHasRowVersion
{
    private readonly List<OrderItem> _items = new();

    private Order()
    {
    }

    public Order(Table table, int waiterId, int guestCount, string? notes)
    {
        if (guestCount < 1)
        {
            throw new DomainException("At least one guest is required.");
        }

        TableId = table.Id;
        Table = table;
        WaiterId = waiterId;
        GuestCount = guestCount;
        Notes = Clean(notes);
        Status = OrderStatus.Draft;
        OpenedOnOccupiedTable = table.Status == TableStatus.Occupied;
    }

    public int OrderNumber { get; private set; }
    public int TableId { get; private set; }
    public Table? Table { get; private set; }
    public int WaiterId { get; private set; }
    public int GuestCount { get; private set; }
    public OrderStatus Status { get; private set; }
    public string? Notes { get; private set; }
    public DateTime? SubmittedAt { get; private set; }
    public DateTime? ServedAt { get; private set; }
    public DateTime? BillRequestedAt { get; private set; }
    public DateTime? ClosedAt { get; private set; }
    public DateTime? CancelledAt { get; private set; }
    public int? CancelledBy { get; private set; }
    public string? CancelReason { get; private set; }

    // Guests were seated before the order was opened, so cancelling the order leaves them seated.
    public bool OpenedOnOccupiedTable { get; private set; }

    public uint RowVersion { get; set; }

    public IReadOnlyCollection<OrderItem> Items => _items;

    public int CurrentBatch => _items.Count == 0 ? 0 : _items.Max(i => i.BatchNumber);

    public IEnumerable<OrderItem> ActiveItems => _items.Where(i => i.Status != OrderItemStatus.Cancelled);

    public decimal ApproxSubtotal => ActiveItems.Sum(i => i.LineTotal);

    public void UpdateDetails(int guestCount, string? notes)
    {
        if (OrderStateMachine.IsTerminal(Status) || Status == OrderStatus.Paid)
        {
            throw new DomainException($"Order {OrderNumber} is {Status} and can no longer be changed.", ErrorCodes.OrderLocked);
        }

        if (guestCount < 1)
        {
            throw new DomainException("At least one guest is required.");
        }

        GuestCount = guestCount;
        Notes = Clean(notes);
    }

    public void ReplaceDraftItems(IEnumerable<OrderItem> items)
    {
        if (Status != OrderStatus.Draft)
        {
            throw new DomainException("Items can be replaced only while the order is a draft.", ErrorCodes.InvalidStateTransition);
        }

        _items.Clear();
        foreach (var item in items)
        {
            item.AssignToBatch(1, OrderItemStatus.Draft);
            _items.Add(item);
        }
    }

    public void Submit(DateTime nowUtc)
    {
        if (Status != OrderStatus.Draft)
        {
            throw new DomainException($"Order {OrderNumber} was already sent to the kitchen.", ErrorCodes.InvalidStateTransition);
        }

        if (_items.Count == 0)
        {
            throw new DomainException("Add at least one item before sending the order.");
        }

        foreach (var item in _items)
        {
            item.MarkSent();
        }

        Status = OrderStatus.Submitted;
        SubmittedAt = nowUtc;
    }

    public int AppendBatch(IReadOnlyCollection<OrderItem> items)
    {
        if (!OrderStateMachine.IsInKitchenBand(Status))
        {
            throw Status == OrderStatus.Draft
                ? new DomainException("The order is still a draft: change its items instead.", ErrorCodes.InvalidStateTransition)
                : new DomainException($"Order {OrderNumber} is {Status} and can no longer take items.", ErrorCodes.OrderLocked);
        }

        if (items.Count == 0)
        {
            throw new DomainException("Add at least one item.");
        }

        var batch = CurrentBatch + 1;
        foreach (var item in items)
        {
            item.AssignToBatch(batch, OrderItemStatus.Sent);
            _items.Add(item);
        }

        return batch;
    }

    public bool ApplyKitchenStatus(OrderStatus derived, DateTime nowUtc)
    {
        if (!OrderStateMachine.IsInKitchenBand(Status) || !OrderStateMachine.IsInKitchenBand(derived) || derived == Status)
        {
            return false;
        }

        Status = derived;
        ServedAt = derived == OrderStatus.Served ? nowUtc : null;
        return true;
    }

    public void Cancel(int userId, string? reason, DateTime nowUtc)
    {
        if (!OrderStateMachine.CanTransition(Status, OrderStatus.Cancelled))
        {
            throw new DomainException($"Order {OrderNumber} is {Status} and cannot be cancelled.", ErrorCodes.InvalidStateTransition);
        }

        foreach (var item in _items)
        {
            item.Cancel(userId, reason);
        }

        Status = OrderStatus.Cancelled;
        CancelledAt = nowUtc;
        CancelledBy = userId;
        CancelReason = Clean(reason);
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
