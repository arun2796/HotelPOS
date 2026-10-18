using HotelPOS.Contracts.Enums;

namespace HotelPOS.Contracts.Presentation;

/// <summary>Colour family of a status. Both clients map a tone to their own brushes / CSS variables (Phase 21 design system).</summary>
public enum StatusTone
{
    Neutral,
    Info,
    Success,
    Warning,
    Danger,
}

/// <summary>Soft = tinted background with tone text; Solid = tone-coloured background with white text.</summary>
public enum StatusFill
{
    Soft,
    Solid,
}

/// <summary>
/// What a status looks like: the words, the tone, a stable icon name ("check", "clock", "flame", ...) that each client maps
/// to a glyph, and the fill. Colour is never the only signal, so every status carries an icon and a label.
/// </summary>
public readonly record struct StatusDisplay(string Label, StatusTone Tone, string Icon, StatusFill Fill = StatusFill.Soft);

/// <summary>
/// The single status map (docs/design/design-system-plan.md section 4): every enum the UI shows as a badge, chip or tile maps
/// to one <see cref="StatusDisplay"/> here. The switches are exhaustive on purpose: a new enum member throws until it is
/// mapped, which the status presentation tests catch.
/// </summary>
public static class StatusPresentation
{
    public const string SentToKitchen = "Sent to kitchen";

    /// <summary>A ticket past the "late" threshold, or a table whose ticket is late.</summary>
    public static readonly StatusDisplay Delayed = new("Delayed", StatusTone.Danger, "alert");

    public static readonly StatusDisplay Connected = new("Connected", StatusTone.Success, "check");
    public static readonly StatusDisplay Connecting = new("Connecting", StatusTone.Warning, "refresh");
    public static readonly StatusDisplay Reconnecting = new("Reconnecting", StatusTone.Warning, "refresh");
    public static readonly StatusDisplay Syncing = new("Syncing", StatusTone.Info, "refresh");
    public static readonly StatusDisplay Offline = new("Offline", StatusTone.Danger, "wifi-off");

    public static StatusDisplay For(OrderStatus status) => status switch
    {
        OrderStatus.Draft => new("Draft", StatusTone.Neutral, "pencil"),
        OrderStatus.Submitted => new(SentToKitchen, StatusTone.Info, "send"),
        OrderStatus.Accepted => new("Accepted", StatusTone.Info, "check"),
        OrderStatus.Preparing => new("Preparing", StatusTone.Warning, "flame"),
        OrderStatus.Ready => new("Ready", StatusTone.Success, "check", StatusFill.Solid),
        OrderStatus.Served => new("Served", StatusTone.Info, "utensils"),
        OrderStatus.BillRequested => new("Bill requested", StatusTone.Warning, "receipt"),
        OrderStatus.Billed => new("Awaiting payment", StatusTone.Warning, "clock"),
        OrderStatus.Paid => new("Paid", StatusTone.Success, "check", StatusFill.Solid),
        OrderStatus.Completed => new("Completed", StatusTone.Success, "check", StatusFill.Solid),
        OrderStatus.Cancelled => new("Cancelled", StatusTone.Danger, "ban"),
        _ => throw Unmapped(status),
    };

    public static StatusDisplay For(OrderType type) => type switch
    {
        OrderType.Takeaway => new("Takeaway", StatusTone.Warning, "shopping-bag"),
        OrderType.Delivery => new("Delivery", StatusTone.Success, "truck"),
        _ => new("Dine-in", StatusTone.Info, "utensils"),
    };

    public static StatusDisplay For(ReservationStatus status) => status switch
    {
        ReservationStatus.Booked => new("Booked", StatusTone.Info, "calendar"),
        ReservationStatus.Seated => new("Seated", StatusTone.Success, "check", StatusFill.Solid),
        ReservationStatus.Cancelled => new("Cancelled", StatusTone.Danger, "ban"),
        ReservationStatus.NoShow => new("No-show", StatusTone.Warning, "alert"),
        _ => throw Unmapped(status),
    };

    public static StatusDisplay For(KitchenOrderStatus status) => status switch
    {
        KitchenOrderStatus.New => new("New", StatusTone.Info, "bell"),
        KitchenOrderStatus.Accepted => new("Accepted", StatusTone.Info, "check"),
        KitchenOrderStatus.Preparing => new("Preparing", StatusTone.Warning, "flame"),
        KitchenOrderStatus.Ready => new("Ready", StatusTone.Success, "check", StatusFill.Solid),
        KitchenOrderStatus.Completed => new("Served", StatusTone.Success, "utensils"),
        KitchenOrderStatus.Cancelled => new("Cancelled", StatusTone.Danger, "ban"),
        _ => throw Unmapped(status),
    };

    public static StatusDisplay For(OrderItemStatus status) => status switch
    {
        OrderItemStatus.Draft => new("Not sent", StatusTone.Neutral, "pencil"),
        OrderItemStatus.Sent => new(SentToKitchen, StatusTone.Info, "send"),
        OrderItemStatus.Cancelled => new("Cancelled", StatusTone.Danger, "ban"),
        _ => throw Unmapped(status),
    };

    /// <summary>Available is the soft success fill and Ready the solid one, so the two greens stay apart (D8).</summary>
    public static StatusDisplay For(TableStatus status) => status switch
    {
        TableStatus.Available => new("Available", StatusTone.Success, "circle"),
        TableStatus.Occupied => new("Occupied", StatusTone.Info, "users"),
        TableStatus.Ordering => new("Ordering", StatusTone.Info, "pencil"),
        TableStatus.Preparing => new("Preparing", StatusTone.Warning, "flame"),
        TableStatus.Ready => new("Ready", StatusTone.Success, "check", StatusFill.Solid),
        TableStatus.Billing => new("Bill requested", StatusTone.Warning, "receipt"),
        TableStatus.OutOfService => new("Out of service", StatusTone.Neutral, "ban"),
        _ => throw Unmapped(status),
    };

    public static StatusDisplay For(BillStatus status) => status switch
    {
        BillStatus.Open => new("Open", StatusTone.Info, "receipt"),
        BillStatus.Finalized => new("Awaiting payment", StatusTone.Warning, "clock"),
        BillStatus.Settled => new("Paid", StatusTone.Success, "check"),
        BillStatus.Voided => new("Voided", StatusTone.Danger, "ban"),
        _ => throw Unmapped(status),
    };

    public static StatusDisplay For(PaymentStatus status) => status switch
    {
        PaymentStatus.Pending => new("Unpaid", StatusTone.Warning, "clock"),
        PaymentStatus.PartiallyPaid => new("Partially paid", StatusTone.Warning, "clock"),
        PaymentStatus.Paid => new("Paid", StatusTone.Success, "check"),
        PaymentStatus.Refunded => new("Refunded", StatusTone.Info, "undo"),
        PaymentStatus.Cancelled => new("Cancelled", StatusTone.Danger, "ban"),
        _ => throw Unmapped(status),
    };

    public static StatusDisplay For(PaymentRecordStatus status) => status switch
    {
        PaymentRecordStatus.Completed => new("Received", StatusTone.Success, "check"),
        PaymentRecordStatus.Refunded => new("Refunded", StatusTone.Info, "undo"),
        PaymentRecordStatus.Voided => new("Voided", StatusTone.Danger, "ban"),
        _ => throw Unmapped(status),
    };

    public static StatusDisplay For(CashShiftStatus status) => status switch
    {
        CashShiftStatus.Open => new("Open", StatusTone.Success, "circle"),
        CashShiftStatus.HandedOver => new("Handed over", StatusTone.Info, "swap"),
        CashShiftStatus.Closed => new("Closed", StatusTone.Neutral, "lock"),
        CashShiftStatus.ForceClosed => new("Force-closed", StatusTone.Warning, "alert"),
        _ => throw Unmapped(status),
    };

    public static StatusDisplay For(BusinessDayStatus status) => status switch
    {
        BusinessDayStatus.Closed => new("Closed", StatusTone.Neutral, "lock"),
        BusinessDayStatus.Reopened => new("Reopened", StatusTone.Warning, "undo"),
        _ => throw Unmapped(status),
    };

    public static StatusDisplay For(BackupStatus status) => status switch
    {
        BackupStatus.Running => new("Running", StatusTone.Info, "refresh"),
        BackupStatus.Succeeded => new("Succeeded", StatusTone.Success, "check"),
        BackupStatus.Failed => new("Failed", StatusTone.Danger, "alert"),
        _ => throw Unmapped(status),
    };

    private static ArgumentOutOfRangeException Unmapped<T>(T status) where T : struct, Enum =>
        new(nameof(status), status, $"{typeof(T).Name}.{status} has no status presentation; add it to StatusPresentation.");
}
