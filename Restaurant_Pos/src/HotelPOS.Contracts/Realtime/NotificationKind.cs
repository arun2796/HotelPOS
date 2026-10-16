using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Security;

namespace HotelPOS.Contracts.Realtime;

/// <summary>What a notification in the bell is about. Built client-side from hub events (Phase 20.1).</summary>
public enum NotificationKind
{
    TicketCreated = 1,
    OrderAccepted,
    OrderPreparing,
    OrderReady,
    TicketDelayed,
    BillRequested,
    BillClaimed,
    PaymentCompleted,
    DeviceOnline,
    DeviceOffline,
    ServerNotice,
    PeriodChanged,
    DayEndWarning,
    PrintFailed,
    PaymentFailed,
}

public enum NotificationTargetType
{
    Order = 1,
    Bill,
    Table,
    Device,
    Ticket,
}

/// <summary>The thing a notification is about; a tap opens the screen that shows it.</summary>
public sealed record NotificationTarget(NotificationTargetType Type, string Id);

/// <summary>A notification before a client stores it. <see cref="Key"/> collapses repeats of the same event.</summary>
public sealed record NotificationDraft(
    NotificationKind Kind,
    NoticeLevel Level,
    string Title,
    string Body,
    string Key,
    NotificationTarget? Target = null,
    string? SentBy = null)
{
    /// <summary>One line for lists and toasts.</summary>
    public string Message => string.IsNullOrWhiteSpace(Body) ? Title : $"{Title} ({Body})";
}

/// <summary>Who is signed in on this client; decides which events become notifications.</summary>
public sealed record NotificationAudience(int UserId, IReadOnlyCollection<string> Roles)
{
    public bool Is(string role) => Roles.Contains(role, StringComparer.OrdinalIgnoreCase);

    public bool IsManagement => Is(Security.Roles.Admin) || Is(Security.Roles.Manager);

    public bool IsBilling => Is(Security.Roles.Cashier) || IsManagement;
}

/// <summary>
/// The one place that decides, for a signed-in role, which hub events become notifications and what they say.
/// Desktop and browser both call it, so the two bells never disagree. Returns null when the event is not for this user.
/// </summary>
public static class NotificationMapper
{
    public static NotificationDraft? OrderProgress(string eventName, OrderProgressEvent e, NotificationAudience who)
    {
        // The hub also sends every waiter's progress to role:waiter; a waiter only cares about their own tables.
        if (!who.Is(Roles.Waiter) || e.WaiterId != who.UserId)
        {
            return null;
        }

        var table = new NotificationTarget(NotificationTargetType.Table, e.TableId.ToString());
        var code = e.TableCode.ToUpperInvariant();
        return eventName switch
        {
            HubEvents.OrderReady => new NotificationDraft(NotificationKind.OrderReady, NoticeLevel.Warning,
                $"TABLE {code} — ORDER READY",
                e.ReadyTicketNumbers.Count == 0 ? string.Empty : $"Ticket {string.Join(", ", e.ReadyTicketNumbers)}",
                $"ready:{e.OrderId}", table),
            HubEvents.OrderAccepted => new NotificationDraft(NotificationKind.OrderAccepted, NoticeLevel.Info,
                $"Table {code} — kitchen accepted the order", string.Empty, $"accepted:{e.OrderId}", table),
            HubEvents.OrderPreparing => new NotificationDraft(NotificationKind.OrderPreparing, NoticeLevel.Info,
                $"Table {code} — being prepared", string.Empty, $"preparing:{e.OrderId}", table),
            _ => null,
        };
    }

    public static NotificationDraft? TicketCreated(KitchenTicketCreatedEvent e, NotificationAudience who) =>
        who.Is(Roles.Kitchen)
            ? new NotificationDraft(NotificationKind.TicketCreated, NoticeLevel.Info,
                $"New ticket {e.TicketNumber} — table {e.TableCode.ToUpperInvariant()}", $"{e.ItemCount} items",
                $"ticket:{e.TicketId}", new NotificationTarget(NotificationTargetType.Ticket, e.TicketId.ToString()))
            : null;

    public static NotificationDraft? BillRequested(BillRequestedEvent e, NotificationAudience who) =>
        who.IsBilling
            ? new NotificationDraft(NotificationKind.BillRequested, NoticeLevel.Warning,
                $"Table {e.TableCode.ToUpperInvariant()} asked for the bill",
                string.IsNullOrWhiteSpace(e.WaiterName) ? $"Bill {e.BillNumber}" : $"Bill {e.BillNumber}, {e.WaiterName}",
                $"bill-requested:{e.BillId}", new NotificationTarget(NotificationTargetType.Bill, e.BillId.ToString()))
            : null;

    public static NotificationDraft? BillUpdated(BillUpdatedEvent e, NotificationAudience who)
    {
        if (e.ChangeType != BillChangeTypes.Claimed || !who.IsBilling)
        {
            return null;
        }

        var by = string.IsNullOrWhiteSpace(e.ClaimedByDevice) ? "a terminal" : e.ClaimedByDevice;
        return new NotificationDraft(NotificationKind.BillClaimed, NoticeLevel.Info,
            $"Table {e.TableCode.ToUpperInvariant()} bill claimed by {by}", string.Empty,
            $"bill-claimed:{e.BillId}", new NotificationTarget(NotificationTargetType.Bill, e.BillId.ToString()));
    }

    public static NotificationDraft? PaymentCompleted(PaymentCompletedEvent e, NotificationAudience who) =>
        who.IsBilling
            ? new NotificationDraft(NotificationKind.PaymentCompleted, NoticeLevel.Info,
                $"Table {e.TableCode.ToUpperInvariant()} paid",
                string.IsNullOrWhiteSpace(e.InvoiceNumber) ? $"{e.GrandTotal:0.00}" : $"{e.InvoiceNumber}, {e.GrandTotal:0.00}",
                $"payment:{e.BillId}", new NotificationTarget(NotificationTargetType.Bill, e.BillId.ToString()))
            : null;

    public static NotificationDraft? DeviceStatus(DeviceStatusChangedEvent e, NotificationAudience who)
    {
        if (!who.IsManagement)
        {
            return null;
        }

        var target = new NotificationTarget(NotificationTargetType.Device, e.DeviceId.ToString());
        return e.IsOnline
            ? new NotificationDraft(NotificationKind.DeviceOnline, NoticeLevel.Info, $"{e.DeviceName} is online", string.Empty,
                $"device:{e.DeviceId}", target)
            : new NotificationDraft(NotificationKind.DeviceOffline, NoticeLevel.Warning, $"{e.DeviceName} went offline", string.Empty,
                $"device:{e.DeviceId}", target);
    }

    /// <summary>Everyone sees broadcasts; the device-disabled order is handled by the shell, not shown.</summary>
    public static NotificationDraft? ServerNotice(ServerNoticeEvent e) =>
        e.Code == Common.ErrorCodes.DeviceDisabled || string.IsNullOrWhiteSpace(e.Message)
            ? null
            : new NotificationDraft(NotificationKind.ServerNotice, e.Level,
                string.IsNullOrWhiteSpace(e.SentBy) ? e.Message : $"{e.Message} ({e.SentBy})", string.Empty,
                $"notice:{e.Message}", null, e.SentBy);

    public static NotificationDraft PeriodChanged(PeriodStatusChangedEvent e)
    {
        var by = string.IsNullOrWhiteSpace(e.ChangedBy) ? "an administrator" : e.ChangedBy;
        return e.Closed
            ? new NotificationDraft(NotificationKind.PeriodChanged, NoticeLevel.Warning,
                $"GST period {e.Label} closed by {by} — bills dated in it can no longer be changed.", string.Empty,
                $"period:{e.Label}:closed", null, e.ChangedBy)
            : new NotificationDraft(NotificationKind.PeriodChanged, NoticeLevel.Info,
                $"GST period {e.Label} reopened by {by} — bills dated in it can change again.", string.Empty,
                $"period:{e.Label}:open", null, e.ChangedBy);
    }
}
