using HotelPOS.Contracts.Security;

namespace HotelPOS.Contracts.Realtime;

/// <summary>What one terminal does when a notification of one kind arrives: show a toast, play a sound (and vibrate where it can).</summary>
public sealed record NotificationPreference(bool Toast, bool Sound);

/// <summary>
/// Defaults for a terminal that has never been configured (Phase 20.2): every kind toasts; a sound plays only for a new
/// ticket on a kitchen terminal, a bill request on a cashier terminal, and always for a failed print. Shared by the desktop
/// and the browser/Android clients so both start the same.
/// </summary>
public static class NotificationPreferenceDefaults
{
    public static IReadOnlyList<NotificationKind> AllKinds { get; } = Enum.GetValues<NotificationKind>();

    public static NotificationPreference For(NotificationKind kind, IEnumerable<string> roles)
    {
        var held = roles.ToList();
        bool Has(string role) => held.Contains(role, StringComparer.OrdinalIgnoreCase);

        var sound = kind switch
        {
            NotificationKind.TicketCreated => Has(Roles.Kitchen),
            NotificationKind.BillRequested => Has(Roles.Cashier),
            NotificationKind.PrintFailed => true,
            _ => false,
        };
        return new NotificationPreference(Toast: true, Sound: sound);
    }

    /// <summary>Plain-language name of a kind for the Settings lists.</summary>
    public static string Label(NotificationKind kind) => kind switch
    {
        NotificationKind.TicketCreated => "New kitchen ticket",
        NotificationKind.OrderAccepted => "Kitchen accepted my order",
        NotificationKind.OrderPreparing => "Kitchen is preparing my order",
        NotificationKind.OrderReady => "Order ready",
        NotificationKind.TicketDelayed => "Ticket running late",
        NotificationKind.BillRequested => "Bill requested",
        NotificationKind.BillClaimed => "Bill claimed by a terminal",
        NotificationKind.PaymentCompleted => "Payment completed",
        NotificationKind.DeviceOnline => "Device online",
        NotificationKind.DeviceOffline => "Device offline",
        NotificationKind.ServerNotice => "Message from the server",
        NotificationKind.PeriodChanged => "GST period closed or reopened",
        NotificationKind.DayEndWarning => "Day end is due",
        NotificationKind.PrintFailed => "Print failed",
        NotificationKind.PaymentFailed => "Payment failed",
        _ => kind.ToString(),
    };
}
