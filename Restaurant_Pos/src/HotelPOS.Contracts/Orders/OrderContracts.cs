using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;

namespace HotelPOS.Contracts.Orders;

public static class OrderLimits
{
    public const int MaxQuantity = 99;
    public const int MaxItemsPerRequest = 100;
    public const int NotesMaxLength = 500;
    public const int ReasonMaxLength = 300;
    public const int CustomerNameMaxLength = 200;
    public const int CustomerPhoneMaxLength = 20;
    public const int DeliveryAddressMaxLength = 500;
}

public sealed record OrderItemInput
{
    public int MenuItemId { get; init; }
    public int Quantity { get; init; } = 1;
    public string? Notes { get; init; }
    public IReadOnlyList<int> ModifierOptionIds { get; init; } = Array.Empty<int>();

    // Takeaway packed with a dine-in meal: the item's packaging charge is added and the KOT is marked "Parcel".
    public bool IsParcel { get; init; }

    // "4A": the seat the dish is for (table code + letter, limited by the table's capacity). Null = whole table.
    public string? SeatCode { get; init; }
}

public sealed record CreateOrderRequest
{
    public OrderType OrderType { get; init; } = OrderType.DineIn;
    public int? TableId { get; init; }
    public int GuestCount { get; init; }
    public string? Notes { get; init; }
    public string? CustomerName { get; init; }
    public string? CustomerPhone { get; init; }
    public string? DeliveryAddress { get; init; }
    public string? DeliveryNotes { get; init; }
    public IReadOnlyList<OrderItemInput> Items { get; init; } = Array.Empty<OrderItemInput>();
    public bool Submit { get; init; }
}

public sealed record ReplaceOrderItemsRequest
{
    public IReadOnlyList<OrderItemInput> Items { get; init; } = Array.Empty<OrderItemInput>();
    public string RowVersion { get; init; } = string.Empty;
}

public sealed record AppendOrderItemsRequest
{
    public IReadOnlyList<OrderItemInput> Items { get; init; } = Array.Empty<OrderItemInput>();
}

public sealed record UpdateOrderRequest
{
    public int GuestCount { get; init; }
    public string? Notes { get; init; }
    public string? CustomerName { get; init; }
    public string? CustomerPhone { get; init; }
    public string? DeliveryAddress { get; init; }
    public string? DeliveryNotes { get; init; }
    public string RowVersion { get; init; } = string.Empty;
}

public sealed record CancelOrderRequest
{
    public string? Reason { get; init; }
}

public sealed record OrderQuery : PagedQuery
{
    public OrderType? OrderType { get; init; }
    public OrderStatus? Status { get; init; }
    public int? TableId { get; init; }
    public int? WaiterId { get; init; }
    public bool? Mine { get; init; }
    public DateTime? FromUtc { get; init; }
    public DateTime? ToUtc { get; init; }
}

public sealed record OrderSummaryDto
{
    public int Id { get; init; }
    public int OrderNumber { get; init; }
    public OrderType OrderType { get; init; }
    public int? TableId { get; init; }
    public string TableCode { get; init; } = string.Empty;
    public string? CustomerName { get; init; }
    public string? CustomerPhone { get; init; }
    public int WaiterId { get; init; }
    public string WaiterName { get; init; } = string.Empty;
    public int GuestCount { get; init; }
    public OrderStatus Status { get; init; }
    public int ItemCount { get; init; }
    public decimal ApproxTotal { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? SubmittedAtUtc { get; init; }
}

public sealed record OrderDetailDto
{
    public int Id { get; init; }
    public int OrderNumber { get; init; }
    public OrderType OrderType { get; init; }
    public int? TableId { get; init; }
    public string TableCode { get; init; } = string.Empty;
    public string? CustomerName { get; init; }
    public string? CustomerPhone { get; init; }
    public string? DeliveryAddress { get; init; }
    public string? DeliveryNotes { get; init; }
    public int WaiterId { get; init; }
    public string WaiterName { get; init; } = string.Empty;
    public int GuestCount { get; init; }
    public string? Notes { get; init; }
    public OrderStatus Status { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? SubmittedAtUtc { get; init; }
    public DateTime? CancelledAtUtc { get; init; }
    public string? CancelReason { get; init; }
    public IReadOnlyList<OrderItemDto> Items { get; init; } = Array.Empty<OrderItemDto>();
    public IReadOnlyList<OrderTicketDto> Tickets { get; init; } = Array.Empty<OrderTicketDto>();
    public IReadOnlyList<SeatGroupStatusDto> SeatGroups { get; init; } = Array.Empty<SeatGroupStatusDto>();
    public OrderBillDto? Bill { get; init; }

    public decimal ApproxSubtotal { get; init; }

    public bool CanModify { get; init; }
    public string RowVersion { get; init; } = string.Empty;
}

public sealed record OrderItemDto
{
    public int Id { get; init; }
    public int BatchNumber { get; init; }
    public int MenuItemId { get; init; }
    public string ItemName { get; init; } = string.Empty;
    public decimal UnitPrice { get; init; }
    public int Quantity { get; init; }
    public string? Notes { get; init; }
    public decimal TaxRatePercent { get; init; }
    public int PreparationStationId { get; init; }
    public OrderItemStatus Status { get; init; }
    public bool IsParcel { get; init; }
    public decimal PackagingCharge { get; init; }
    public string? SeatCode { get; init; }
    public IReadOnlyList<OrderItemModifierDto> Modifiers { get; init; } = Array.Empty<OrderItemModifierDto>();
    public decimal LineTotal { get; init; }
    public int? TicketId { get; init; }
    public KitchenOrderStatus? TicketStatus { get; init; }
}

public sealed record OrderTicketDto
{
    public int Id { get; init; }
    public string TicketNumber { get; init; } = string.Empty;
    public int BatchNumber { get; init; }
    public string StationCode { get; init; } = string.Empty;
    public KitchenOrderStatus Status { get; init; }

    // The seat group this ticket is for; empty for the whole table. Each seat group has tickets of its own.
    public string? SeatCode { get; init; }
}

// Where one seat group of the order stands in the kitchen: its dishes only, never another group's.
public sealed record SeatGroupStatusDto
{
    public string SeatCode { get; init; } = string.Empty;
    public int ItemCount { get; init; }
    public int Quantity { get; init; }

    // The least advanced of the group's open tickets; null while nothing of it has been sent, or all was cancelled.
    public KitchenOrderStatus? KitchenStatus { get; init; }
}

public sealed record OrderItemModifierDto(int ModifierOptionId, string Name, decimal PriceDelta);
