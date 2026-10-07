namespace HotelPOS.Contracts.Realtime;

/// <summary>SignalR hub route and method names.</summary>
public static class HubRoutes
{
    public const string Restaurant = "/hubs/restaurant";
}

/// <summary>Server-to-client event names. See docs/04-realtime-events.md.</summary>
public static class HubEvents
{
    public const string TableStatusChanged = "TableStatusChanged";
    public const string MenuChanged = "MenuChanged";
    public const string OrderCreated = "OrderCreated";
    public const string OrderUpdated = "OrderUpdated";
    public const string OrderCancelled = "OrderCancelled";
    public const string KitchenTicketCreated = "KitchenTicketCreated";
    public const string KitchenTicketUpdated = "KitchenTicketUpdated";
    public const string OrderAccepted = "OrderAccepted";
    public const string OrderPreparing = "OrderPreparing";
    public const string OrderReady = "OrderReady";
    public const string OrderServed = "OrderServed";
    public const string BillRequested = "BillRequested";
    public const string BillUpdated = "BillUpdated";
    public const string PaymentCompleted = "PaymentCompleted";
    public const string DeviceStatusChanged = "DeviceStatusChanged";
    public const string ServerNotice = "ServerNotice";
}

/// <summary>Client-to-server hub methods.</summary>
public static class HubMethods
{
    public const string Ping = "Ping";
    public const string JoinStation = "JoinStation";
    public const string LeaveStation = "LeaveStation";
}

/// <summary>
/// Base shape of every real-time payload. Events are notifications only: clients fetch the
/// authoritative state from the REST API when they need details.
/// </summary>
public abstract record RealtimeEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredAtUtc { get; init; }
    public int EntityId { get; init; }
    public string? EntityVersion { get; init; }
}
