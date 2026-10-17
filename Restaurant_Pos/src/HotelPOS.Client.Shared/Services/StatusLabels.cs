using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Presentation;

namespace HotelPOS.Client.Services;

/// <summary>
/// Waiter-screen words and chip classes for order, kitchen-ticket, line and table states. A thin delegate to
/// <see cref="StatusPresentation"/> (the single status map, Phase 21): a ticket the kitchen has not accepted yet and a
/// submitted order both read "Sent to kitchen" on the waiter side; Ready is good news (green), never the error red.
/// </summary>
public static class StatusLabels
{
    public const string SentToKitchen = StatusPresentation.SentToKitchen;

    public static string Order(OrderStatus status) => StatusPresentation.For(status).Label;

    public static string OrderChip(OrderStatus status) => Chip(StatusPresentation.For(status));

    public static string Ticket(KitchenOrderStatus status) =>
        status == KitchenOrderStatus.New ? SentToKitchen : StatusPresentation.For(status).Label;

    public static string TicketChip(KitchenOrderStatus status) => Chip(StatusPresentation.For(status));

    /// <summary>A line before it has a ticket (not sent yet) or after it was cancelled.</summary>
    public static string Line(OrderItemStatus status) => StatusPresentation.For(status).Label;

    public static string Table(TableStatus status) => StatusPresentation.For(status).Label;

    /// <summary>The <c>st-*</c> suffix of a table state (<c>st-available</c>, <c>st-outofservice</c>, ...).</summary>
    public static string TableClass(TableStatus status) => status.ToString().ToLowerInvariant();

    /// <summary>The <c>chip</c> modifier class for a tone: <c>success</c>, <c>warning</c>, <c>danger</c>, <c>info</c>; neutral has none.</summary>
    public static string Chip(StatusDisplay display) => Tone(display.Tone);

    public static string Tone(StatusTone tone) => tone switch
    {
        StatusTone.Success => "success",
        StatusTone.Warning => "warning",
        StatusTone.Danger => "danger",
        StatusTone.Info => "info",
        _ => string.Empty,
    };
}
