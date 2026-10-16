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
    public const string TemplateActivated = "TemplateActivated";
    public const string DocumentPrinted = "DocumentPrinted";
    public const string ShiftChanged = "ShiftChanged";
    public const string BusinessDayClosed = "BusinessDayClosed";
    public const string BusinessDayReopened = "BusinessDayReopened";
    public const string PeriodStatusChanged = "PeriodStatusChanged";
    public const string PrintFailed = "PrintFailed";
    public const string PaymentFailed = "PaymentFailed";
    public const string DayEndDue = "DayEndDue";
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

// A reserved print copy was reported failed. Sent to the reporting terminal and to managers.
public sealed record PrintFailedEvent : RealtimeEvent
{
    public Print.PrintDocumentType DocumentType { get; init; }
    public int DocumentId { get; init; }
    public int CopyNumber { get; init; }
    public Print.PrintOutput Output { get; init; }
    public string? Error { get; init; }
    public string? DeviceName { get; init; }

    // The number people know the document by (invoice number, ticket number); null for a document without one.
    public string? DocumentNumber { get; init; }
}

// A payment was rejected (validation or a changed bill). Sent only to the terminal that tried it; EntityId is the bill.
public sealed record PaymentFailedEvent : RealtimeEvent
{
    public int BillId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

// Reminder that the business day is still open at the configured time. Sent to managers and admins.
public sealed record DayEndDueEvent : RealtimeEvent
{
    public DateOnly BusinessDay { get; init; }
    public string ReminderTime { get; init; } = string.Empty;
}
