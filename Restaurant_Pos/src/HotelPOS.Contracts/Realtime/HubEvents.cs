namespace HotelPOS.Contracts.Realtime;

public static class HubRoutes
{
    public const string Restaurant = "/hubs/restaurant";
}

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

public static class HubMethods
{
    public const string Ping = "Ping";
    public const string JoinStation = "JoinStation";
    public const string LeaveStation = "LeaveStation";
}

public abstract record RealtimeEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredAtUtc { get; init; }
    public int EntityId { get; init; }
    public string? EntityVersion { get; init; }
}
