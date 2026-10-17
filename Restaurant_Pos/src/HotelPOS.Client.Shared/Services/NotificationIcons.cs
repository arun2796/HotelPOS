using HotelPOS.Contracts.Realtime;

namespace HotelPOS.Client.Services;

/// <summary>Phase 21 icon name for each bell entry kind (replaces the Unicode glyphs of the Phase 20 panel).</summary>
public static class NotificationIcons
{
    public static string For(NotificationKind kind) => kind switch
    {
        NotificationKind.TicketCreated => "receipt",
        NotificationKind.OrderAccepted => "check",
        NotificationKind.OrderPreparing => "flame",
        NotificationKind.TicketDelayed => "alert",
        NotificationKind.OrderReady => "utensils",
        NotificationKind.BillRequested or NotificationKind.BillClaimed => "receipt",
        NotificationKind.PaymentCompleted => "check",
        NotificationKind.PaymentFailed or NotificationKind.PrintFailed => "ban",
        NotificationKind.DeviceOnline => "signal",
        NotificationKind.DeviceOffline => "wifi-off",
        NotificationKind.DayEndWarning or NotificationKind.PeriodChanged => "clock",
        _ => "bell",
    };
}
