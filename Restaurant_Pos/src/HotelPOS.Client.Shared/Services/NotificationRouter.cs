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
    private readonly List<IDisposable> _subscriptions = new();

    public NotificationRouter(WebRealtimeClient realtime, WebAuthState auth, NotificationCenter center, ToastService toasts)
    {
        _realtime = realtime;
        _auth = auth;
        _center = center;
        _toasts = toasts;
    }

    public async Task StartAsync()
    {
        if (_subscriptions.Count > 0 || !_auth.IsSignedIn)
        {
            return;
        }

        await _center.LoadAsync();
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
    }

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

    private NotificationAudience? Audience() =>
        _auth.User is { } user ? new NotificationAudience(user.Id, user.Roles.ToList()) : null;

    private void Publish(NotificationDraft? draft)
    {
        if (draft is null || !_center.Add(draft))
        {
            // Not for this user, or a repeat that only updated its bell entry.
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
