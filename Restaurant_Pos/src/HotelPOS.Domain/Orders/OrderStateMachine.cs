using HotelPOS.Contracts.Enums;

namespace HotelPOS.Domain.Orders;

public static class OrderStateMachine
{
    private static readonly OrderStatus[] KitchenBand =
    {
        OrderStatus.Submitted, OrderStatus.Accepted, OrderStatus.Preparing, OrderStatus.Ready, OrderStatus.Served,
    };

    public static bool IsInKitchenBand(OrderStatus status) => KitchenBand.Contains(status);

    public static bool IsActive(OrderStatus status) =>
        status is not (OrderStatus.Paid or OrderStatus.Completed or OrderStatus.Cancelled);

    public static bool IsTerminal(OrderStatus status) => status is OrderStatus.Completed or OrderStatus.Cancelled;

    public static bool CanTransition(OrderStatus from, OrderStatus to)
    {
        if (from == to)
        {
            return false;
        }

        // Within the kitchen band the status is derived from tickets and may move both ways.
        if (IsInKitchenBand(from) && IsInKitchenBand(to))
        {
            return true;
        }

        return (from, to) switch
        {
            (OrderStatus.Draft, OrderStatus.Submitted or OrderStatus.Cancelled) => true,
            (OrderStatus.Submitted or OrderStatus.Accepted or OrderStatus.Preparing or OrderStatus.Ready, OrderStatus.Cancelled) => true,
            (OrderStatus.Served or OrderStatus.Ready, OrderStatus.BillRequested) => true,
            (OrderStatus.BillRequested, OrderStatus.Billed or OrderStatus.Served) => true,
            (OrderStatus.Billed, OrderStatus.Paid or OrderStatus.Served or OrderStatus.Cancelled) => true,
            (OrderStatus.Paid, OrderStatus.Completed) => true,
            _ => false,
        };
    }

    public static CancelPermission CanCancel(OrderStatus status, bool isManager, bool isOwner) => status switch
    {
        OrderStatus.Draft or OrderStatus.Submitted or OrderStatus.Accepted =>
            isManager || isOwner ? CancelPermission.Allowed : CancelPermission.NotOwner,
        OrderStatus.Preparing or OrderStatus.Ready =>
            isManager ? CancelPermission.AllowedWithReason : CancelPermission.ManagerOnly,
        _ => CancelPermission.NotCancellable,
    };
}

public enum CancelPermission
{
    Allowed,
    AllowedWithReason,
    NotOwner,
    ManagerOnly,
    NotCancellable,
}
