using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;

namespace HotelPOS.Contracts.Orders;

public static class OrderLimits
{
    public const int MaxQuantity = 99;
    public const int MaxItemsPerRequest = 100;
    public const int NotesMaxLength = 500;
    public const int ReasonMaxLength = 300;
}

public sealed record OrderItemInput
{
    public int MenuItemId { get; init; }
    public int Quantity { get; init; } = 1;
    public string? Notes { get; init; }
    public IReadOnlyList<int> ModifierOptionIds { get; init; } = Array.Empty<int>();
}

public sealed record CreateOrderRequest
{
    public int TableId { get; init; }
    public int GuestCount { get; init; }
    public string? Notes { get; init; }
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
    public string RowVersion { get; init; } = string.Empty;
}

public sealed record CancelOrderRequest
{
    public string? Reason { get; init; }
}

public sealed record OrderQuery : PagedQuery
{
    public OrderStatus? Status { get; init; }
    public int? TableId { get; init; }
    public int? WaiterId { get; init; }
    public DateTime? FromUtc { get; init; }
    public DateTime? ToUtc { get; init; }
}

public sealed record OrderSummaryDto
{
    public int Id { get; init; }
    public int OrderNumber { get; init; }
    public int TableId { get; init; }
    public string TableCode { get; init; } = string.Empty;
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
    public int TableId { get; init; }
    public string TableCode { get; init; } = string.Empty;
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
}

public sealed record OrderItemModifierDto(int ModifierOptionId, string Name, decimal PriceDelta);
