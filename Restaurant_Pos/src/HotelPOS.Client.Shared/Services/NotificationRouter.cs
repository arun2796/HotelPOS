using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Realtime;

namespace HotelPOS.Client.Services;

/// <summary>
/// Turns hub events into bell entries and toasts for the signed-in user. Which events a role sees is decided by
/// <see cref="NotificationMapper"/>, shared with the desktop client; this class subscribes, stores and maps a target
/// to the page that shows it.
/// </summary>
public sealed class NotificationRouter : IDisposable
{
    private readonly WebRealtimeClient _realtime;
    private readonly WebAuthState _auth;
    private readonly NotificationCenter _center;
    private readonly ToastService _toasts;
    private readonly NotificationPreferences _preferences;
    private readonly IAlertSignal _signal;
    private readonly List<IDisposable> _subscriptions = new();

    public NotificationRouter(WebRealtimeClient realtime, WebAuthState auth, NotificationCenter center, ToastService toasts,
        NotificationPreferences preferences, IAlertSignal signal)
    {
        _realtime = realtime;
        _auth = auth;
        _center = center;
        _toasts = toasts;
        _preferences = preferences;
        _signal = signal;
    }

    public async Task StartAsync()
    {
        if (_subscriptions.Count > 0 || !_auth.IsSignedIn)
        {
            return;
        }

        await _center.LoadAsync();
        await _preferences.LoadAsync();
        await _realtime.StartAsync();

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
        _subscriptions.Add(_realtime.Subscribe<PeriodStatusChangedEvent>(HubEvents.PeriodStatusChanged,
            e => Publish(NotificationMapper.PeriodChanged(e))));

        // PrintFailed and PaymentFailed are addressed by the server; DayEndDue goes to Manager/Admin (the mapper checks again).
        _subscriptions.Add(_realtime.Subscribe<PrintFailedEvent>(HubEvents.PrintFailed,
            e => Publish(NotificationMapper.PrintFailed(e))));
        _subscriptions.Add(_realtime.Subscribe<PaymentFailedEvent>(HubEvents.PaymentFailed,
            e => Publish(NotificationMapper.PaymentFailed(e))));
        _subscriptions.Add(_realtime.Subscribe<DayEndDueEvent>(HubEvents.DayEndDue,
            e => Publish(Audience() is { } who ? NotificationMapper.DayEndDue(e, who) : null)));
    }

    /// <summary>A kitchen ticket just crossed the late threshold (raised by the kitchen page, not by the server).</summary>
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

    public void Dispose() => Stop();

    /// <summary>The page that shows a target, or null when this client has none (bills and devices live on the desktop).</summary>
    public static string? UrlFor(NotificationTarget? target) => target?.Type switch
    {
        NotificationTargetType.Table when int.TryParse(target.Id, out var id) => $"tables/{id}",
        NotificationTargetType.Table => "tables",
        NotificationTargetType.Order => "my-orders",
        NotificationTargetType.Ticket => "kitchen",
        _ => null,
    };

    private async Task SignalAsync(NotificationKind kind)
    {
        try
        {
            await _signal.SignalAsync(kind);
        }
        catch (Exception)
        {
            // No audio device or no JS runtime (prerender, tests): the bell and toast still tell the user.
        }
    }

    private NotificationAudience? Audience() =>
        _auth.User is { } user ? new NotificationAudience(user.Id, user.Roles.ToList()) : null;

    private void Publish(NotificationDraft? draft)
    {
        if (draft is null || !_center.Add(draft))
        {
            // Not for this user, or a repeat that only updated its bell entry.
            return;
        }

        var preference = _preferences.Get(draft.Kind);
        if (preference.Sound)
        {
            _ = SignalAsync(draft.Kind);
        }

        if (!preference.Toast)
        {
            return;
        }

        switch (draft.Level)
        {
            case NoticeLevel.Critical:
                _toasts.Error(draft.Message);
                break;
            case NoticeLevel.Warning:
                _toasts.Warning(draft.Message);
                break;
            default:
                _toasts.Info(draft.Message);
                break;
        }
    }
}
