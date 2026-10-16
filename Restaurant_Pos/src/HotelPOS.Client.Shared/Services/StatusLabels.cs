using HotelPOS.Contracts.Enums;

namespace HotelPOS.Client.Services;

/// <summary>
/// The one set of words and chip colours for order, kitchen-ticket and line states on the waiter screens, so an order
/// never reads "Submitted" in one place, "New" in another and "BillRequested" in a third. A ticket the kitchen has not
/// accepted yet and a submitted order are both "Sent to kitchen". Ready is good news (green), never the error red.
/// Table states keep the desktop legend's words (Available … Billing) because the map's colours carry the same meaning.
/// </summary>
public static class StatusLabels
{
    public const string SentToKitchen = "Sent to kitchen";

    public static string Order(OrderStatus status) => status switch
    {
        OrderStatus.Draft => "Draft",
        OrderStatus.Submitted => SentToKitchen,
        OrderStatus.Accepted => "Accepted",
        OrderStatus.Preparing => "Preparing",
        OrderStatus.Ready => "Ready",
        OrderStatus.Served => "Served",
        OrderStatus.BillRequested => "Bill requested",
        OrderStatus.Billed => "Billed",
        OrderStatus.Paid => "Paid",
        OrderStatus.Completed => "Completed",
        OrderStatus.Cancelled => "Cancelled",
        _ => status.ToString(),
    };

    public static string OrderChip(OrderStatus status) => status switch
    {
        OrderStatus.Ready or OrderStatus.Paid or OrderStatus.Completed => "success",
        OrderStatus.Preparing or OrderStatus.BillRequested or OrderStatus.Billed => "warning",
        OrderStatus.Cancelled => "danger",
        OrderStatus.Draft or OrderStatus.Served => string.Empty,
        _ => "info",
    };

    public static string Ticket(KitchenOrderStatus status) => status switch
    {
        KitchenOrderStatus.New => SentToKitchen,
        KitchenOrderStatus.Accepted => "Accepted",
        KitchenOrderStatus.Preparing => "Preparing",
        KitchenOrderStatus.Ready => "Ready",
        KitchenOrderStatus.Completed => "Served",
        KitchenOrderStatus.Cancelled => "Cancelled",
        _ => status.ToString(),
    };

    public static string TicketChip(KitchenOrderStatus status) => status switch
    {
        KitchenOrderStatus.Ready or KitchenOrderStatus.Completed => "success",
        KitchenOrderStatus.Preparing => "warning",
        KitchenOrderStatus.Cancelled => "danger",
        _ => "info",
    };

    /// <summary>A line before it has a ticket (not sent yet) or after it was cancelled.</summary>
    public static string Line(OrderItemStatus status) => status switch
    {
        OrderItemStatus.Draft => "Not sent",
        OrderItemStatus.Sent => SentToKitchen,
        OrderItemStatus.Cancelled => "Cancelled",
        _ => status.ToString(),
    };

    public static string Table(TableStatus status) => status switch
    {
        TableStatus.OutOfService => "Out of service",
        _ => status.ToString(),
    };
}
