using HotelPOS.Contracts.Enums;

namespace HotelPOS.Contracts.Realtime;

public sealed record KitchenTicketCreatedEvent : RealtimeEvent
{
    public int TicketId { get; init; }
    public string TicketNumber { get; init; } = string.Empty;
    public int OrderId { get; init; }
    public string TableCode { get; init; } = string.Empty;
    public int StationId { get; init; }
    public int ItemCount { get; init; }
}

public sealed record KitchenTicketUpdatedEvent : RealtimeEvent
{
    public int TicketId { get; init; }
    public string TicketNumber { get; init; } = string.Empty;
    public int OrderId { get; init; }
    public string TableCode { get; init; } = string.Empty;
    public int StationId { get; init; }
    public KitchenOrderStatus Status { get; init; }
}

// Used for OrderAccepted, OrderPreparing, OrderReady and OrderServed.
public sealed record OrderProgressEvent : RealtimeEvent
{
    public int OrderId { get; init; }
    public int OrderNumber { get; init; }
    public int TableId { get; init; }
    public string TableCode { get; init; } = string.Empty;
    public int WaiterId { get; init; }
    public OrderStatus Status { get; init; }
    public IReadOnlyList<string> ReadyTicketNumbers { get; init; } = Array.Empty<string>();
}
