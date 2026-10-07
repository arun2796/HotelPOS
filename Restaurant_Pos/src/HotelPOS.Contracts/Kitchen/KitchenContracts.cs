using HotelPOS.Contracts.Enums;

namespace HotelPOS.Contracts.Kitchen;

public sealed record KitchenTicketDto
{
    public int Id { get; init; }
    public string TicketNumber { get; init; } = string.Empty;
    public int OrderId { get; init; }
    public int OrderNumber { get; init; }
    public int BatchNumber { get; init; }
    public string TableCode { get; init; } = string.Empty;
    public string WaiterName { get; init; } = string.Empty;
    public int StationId { get; init; }
    public string StationCode { get; init; } = string.Empty;
    public KitchenOrderStatus Status { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? AcceptedAtUtc { get; init; }
    public DateTime? StartedAtUtc { get; init; }
    public DateTime? ReadyAtUtc { get; init; }
    public DateTime? CompletedAtUtc { get; init; }
    public DateTime? CancelledAtUtc { get; init; }
    public IReadOnlyList<KitchenTicketItemDto> Items { get; init; } = Array.Empty<KitchenTicketItemDto>();
    public string RowVersion { get; init; } = string.Empty;
}

public sealed record KitchenTicketItemDto
{
    public int OrderItemId { get; init; }
    public string Name { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public string? Notes { get; init; }
    public IReadOnlyList<string> Modifiers { get; init; } = Array.Empty<string>();
    public bool IsCancelled { get; init; }
}

public sealed record KitchenTicketListDto
{
    public IReadOnlyList<KitchenTicketDto> Tickets { get; init; } = Array.Empty<KitchenTicketDto>();
    public DateTime ServerTimeUtc { get; init; }
}

public sealed record KitchenTicketQuery
{
    public int? StationId { get; init; }
    public KitchenOrderStatus? Status { get; init; }
}

public sealed record CancelOrderItemRequest
{
    public string? Reason { get; init; }
}
