using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Desktop.Modules.Printing;
using HotelPOS.Desktop.Services.Auth;
using HotelPOS.Desktop.Services.Configuration;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Printing;
using HotelPOS.Desktop.Services.Realtime;

namespace HotelPOS.Desktop.Services.Ui;

public interface INotificationRouter
{
    void Start();

    void Stop();

    // Opens the screen a notice is about (table map, billing, kitchen display, devices).
    Task OpenAsync(NotificationTarget target);

    // A kitchen ticket just crossed the late threshold (raised by the kitchen display, not by the server).
    void TicketDelayed(int ticketId, string ticketNumber, string tableCode);

    // This terminal is about to cause the event itself (it is paying the bill): the echo from the hub is not news
    // to it, so the router drops the matching notification once. The screens still refresh from the event.
    void ExpectOwn(NotificationKind kind, NotificationTargetType targetType, string targetId);
}

// Turns hub events into bell entries and toasts for the signed-in user. Which events a role sees is decided by
// NotificationMapper (shared with the browser client); this class only subscribes, stores and navigates.
public sealed class NotificationRouter : INotificationRouter
{
    private static readonly TimeSpan OwnActionWindow = TimeSpan.FromMinutes(2);

    private readonly IRealtimeClient _realtime;
    private readonly IAuthSession _session;
    private readonly NoticeCenter _notices;
    private readonly INotificationService _notifications;
    private readonly INavigationService _navigation;
    private readonly NotificationPreferences _preferences;
    private readonly ISoundPlayer _sound;
    private readonly IClientSettingsService _settings;
    private readonly IPrintQueue _printQueue;
    private readonly List<IDisposable> _subscriptions = new();
    private readonly Dictionary<(NotificationKind, NotificationTargetType, string), DateTime> _ownActions = new();

    public NotificationRouter(
        IRealtimeClient realtime,
        IAuthSession session,
        NoticeCenter notices,
        INotificationService notifications,
        INavigationService navigation,
        NotificationPreferences preferences,
        ISoundPlayer sound,
        IClientSettingsService settings,
        IPrintQueue printQueue)
    {
        _realtime = realtime;
        _session = session;
        _notices = notices;
        _notifications = notifications;
        _navigation = navigation;
        _preferences = preferences;
        _sound = sound;
        _settings = settings;
        _printQueue = printQueue;
    }

    public void Start()
    {
        if (_subscriptions.Count > 0)
        {
            return;
        }

        foreach (var name in new[] { HubEvents.OrderAccepted, HubEvents.OrderPreparing, HubEvents.OrderReady })
        {
            var eventName = name;
            _subscriptions.Add(_realtime.Subscribe<OrderProgressEvent>(eventName,
                e => Publish(Audience() is { } who ? NotificationMapper.OrderProgress(eventName, e, who) : null)));
        }

        _subscriptions.Add(_realtime.Subscribe<KitchenTicketCreatedEvent>(HubEvents.KitchenTicketCreated,
            e => Publish(Audience() is { } who ? NotificationMapper.TicketCreated(e, who) : null)));
        _subscriptions.Add(_realtime.Subscribe<BillRequestedEvent>(HubEvents.BillRequested,
            e => Publish(Audience() is { } who ? NotificationMapper.BillRequested(e, who) : null)));
        _subscriptions.Add(_realtime.Subscribe<BillUpdatedEvent>(HubEvents.BillUpdated,
            e => Publish(Audience() is { } who ? NotificationMapper.BillUpdated(e, who) : null)));
        _subscriptions.Add(_realtime.Subscribe<PaymentCompletedEvent>(HubEvents.PaymentCompleted,
            e => Publish(Audience() is { } who ? NotificationMapper.PaymentCompleted(e, who) : null)));
        _subscriptions.Add(_realtime.Subscribe<DeviceStatusChangedEvent>(HubEvents.DeviceStatusChanged,
            e => Publish(Audience() is { } who ? NotificationMapper.DeviceStatus(e, who) : null)));
        _subscriptions.Add(_realtime.Subscribe<ServerNoticeEvent>(HubEvents.ServerNotice,
            e => Publish(NotificationMapper.ServerNotice(e))));

        // PrintFailed and PaymentFailed are addressed by the server (the device, plus managers for prints), so whoever
        // receives one is meant to see it. DayEndDue goes to Manager/Admin; the mapper checks the role again.
        // The terminal that reported the failure already has the print queue's own "was not printed · Retry" toast,
        // so for it the failure goes to the bell only.
        _subscriptions.Add(_realtime.Subscribe<PrintFailedEvent>(HubEvents.PrintFailed,
            e => Publish(NotificationMapper.PrintFailed(e, DocumentNumberOf(e)), toast: !IsThisDevice(e.DeviceName))));
        _subscriptions.Add(_realtime.Subscribe<PaymentFailedEvent>(HubEvents.PaymentFailed,
            e => Publish(NotificationMapper.PaymentFailed(e))));
        _subscriptions.Add(_realtime.Subscribe<DayEndDueEvent>(HubEvents.DayEndDue,
            e => Publish(Audience() is { } who ? NotificationMapper.DayEndDue(e, who) : null)));

        // Bills dated in a closed GST period can no longer be changed, which a cashier would otherwise only learn
        // from the next refusal, so every role is told wherever it is.
        _subscriptions.Add(_realtime.Subscribe<PeriodStatusChangedEvent>(HubEvents.PeriodStatusChanged,
            e => Publish(NotificationMapper.PeriodChanged(e))));
    }

    public void TicketDelayed(int ticketId, string ticketNumber, string tableCode) =>
        Publish(Audience() is { } who ? NotificationMapper.TicketDelayed(ticketId, ticketNumber, tableCode, who) : null);

    public void Stop()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
    }

    public Task OpenAsync(NotificationTarget target)
    {
        var roles = _session.User?.Roles ?? Array.Empty<string>();
        var isWaiter = roles.Contains(HotelPOS.Contracts.Security.Roles.Waiter, StringComparer.OrdinalIgnoreCase);
        return target.Type switch
        {
            NotificationTargetType.Table when int.TryParse(target.Id, out var tableId) =>
                _navigation.NavigateToAsync(ModuleRegistry.Tables, tableId),
            NotificationTargetType.Table => _navigation.NavigateToAsync(ModuleRegistry.Tables),
            NotificationTargetType.Order => _navigation.NavigateToAsync(isWaiter ? ModuleRegistry.MyOrders : ModuleRegistry.Tables),
            NotificationTargetType.Bill => _navigation.NavigateToAsync(ModuleRegistry.Billing),
            NotificationTargetType.Ticket => _navigation.NavigateToAsync(ModuleRegistry.KitchenDisplay),
            NotificationTargetType.Device => _navigation.NavigateToAsync(ModuleRegistry.Devices),
            // A failed print opens the print log on its Failed filter (cashier and managers have it; others have no such module).
            NotificationTargetType.Print when roles.Any(r => ModuleRegistry.Find(ModuleRegistry.PrintLog)!.Roles.Contains(r, StringComparer.OrdinalIgnoreCase)) =>
                _navigation.NavigateToAsync(ModuleRegistry.PrintLog, new PrintLogContext(PrintLogFilter.Failed)),
            _ => Task.CompletedTask,
        };
    }

    public void ExpectOwn(NotificationKind kind, NotificationTargetType targetType, string targetId)
    {
        lock (_ownActions)
        {
            _ownActions[(kind, targetType, targetId)] = DateTime.UtcNow;
        }
    }

    private NotificationAudience? Audience() =>
        _session.User is { } user ? new NotificationAudience(user.Id, user.Roles.ToList(), _settings.Current.DeviceName) : null;

    private bool IsThisDevice(string? deviceName) =>
        !string.IsNullOrWhiteSpace(deviceName) && string.Equals(deviceName, _settings.Current.DeviceName, StringComparison.OrdinalIgnoreCase);

    // The queue rendered the document before it failed, so it knows the number even when the server did not send one.
    private string? DocumentNumberOf(PrintFailedEvent e) =>
        _printQueue.Failed.FirstOrDefault(f => f.Job.Type == e.DocumentType && f.Job.EntityId == e.DocumentId)?.Job.DocumentNumber;

    private bool IsOwnAction(NotificationDraft draft)
    {
        if (draft.Target is not { } target)
        {
            return false;
        }

        lock (_ownActions)
        {
            var cutoff = DateTime.UtcNow - OwnActionWindow;
            foreach (var stale in _ownActions.Where(p => p.Value < cutoff).Select(p => p.Key).ToList())
            {
                _ownActions.Remove(stale);
            }

            return _ownActions.Remove((draft.Kind, target.Type, target.Id));
        }
    }

    private void Publish(NotificationDraft? draft, bool toast = true)
    {
        if (draft is null || IsOwnAction(draft))
        {
            return;
        }

        var added = _notices.Add(new NoticeItem(draft.Message, draft.SentBy, draft.Level, DateTime.Now)
        {
            Kind = draft.Kind,
            Target = draft.Target,
            Key = draft.Key,
        });
        if (!added)
        {
            // A repeat inside 30 seconds updated the bell entry; do not toast it again.
            return;
        }

        var preference = _preferences.Get(draft.Kind);
        if (preference.Sound)
        {
            _sound.Play(draft.Kind);
        }

        if (!preference.Toast || !toast)
        {
            // The bell entry stays; only the pop-up is switched off for this kind on this terminal.
            return;
        }

        var kind = draft.Level switch
        {
            NoticeLevel.Critical => ToastKind.Error,
            NoticeLevel.Warning => ToastKind.Warning,
            _ => ToastKind.Info,
        };
        if (draft.Target is { } target)
        {
            _notifications.Notify(kind, draft.Message, "Open", () => _ = OpenAsync(target));
            return;
        }

        switch (kind)
        {
            case ToastKind.Error:
                _notifications.Error(draft.Message);
                break;
            case ToastKind.Warning:
                _notifications.Warning(draft.Message);
                break;
            default:
                _notifications.Info(draft.Message);
                break;
        }
    }
}
