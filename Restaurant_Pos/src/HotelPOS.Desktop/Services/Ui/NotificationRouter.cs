using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Desktop.Services.Auth;
using HotelPOS.Desktop.Services.Navigation;
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
}

// Turns hub events into bell entries and toasts for the signed-in user. Which events a role sees is decided by
// NotificationMapper (shared with the browser client); this class only subscribes, stores and navigates.
public sealed class NotificationRouter : INotificationRouter
{
    private readonly IRealtimeClient _realtime;
    private readonly IAuthSession _session;
    private readonly NoticeCenter _notices;
    private readonly INotificationService _notifications;
    private readonly INavigationService _navigation;
    private readonly NotificationPreferences _preferences;
    private readonly ISoundPlayer _sound;
    private readonly List<IDisposable> _subscriptions = new();

    public NotificationRouter(
        IRealtimeClient realtime,
        IAuthSession session,
        NoticeCenter notices,
        INotificationService notifications,
        INavigationService navigation,
        NotificationPreferences preferences,
        ISoundPlayer sound)
    {
        _realtime = realtime;
        _session = session;
        _notices = notices;
        _notifications = notifications;
        _navigation = navigation;
        _preferences = preferences;
        _sound = sound;
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
        _subscriptions.Add(_realtime.Subscribe<PrintFailedEvent>(HubEvents.PrintFailed,
            e => Publish(NotificationMapper.PrintFailed(e))));
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
            _ => Task.CompletedTask,
        };
    }

    private NotificationAudience? Audience() =>
        _session.User is { } user ? new NotificationAudience(user.Id, user.Roles.ToList()) : null;

    private void Publish(NotificationDraft? draft)
    {
        if (draft is null)
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

        if (!preference.Toast)
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
