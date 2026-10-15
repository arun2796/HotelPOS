using HotelPOS.Contracts.Realtime;
using HotelPOS.Desktop.Services.Realtime;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Services.Orders;

public interface IReadyNotifier
{
    void Start();

    void Stop();
}

public sealed class ReadyNotifier : IReadyNotifier
{
    private readonly IRealtimeClient _realtime;
    private readonly INotificationService _notifications;
    private IDisposable? _subscription;

    public ReadyNotifier(IRealtimeClient realtime, INotificationService notifications)
    {
        _realtime = realtime;
        _notifications = notifications;
    }

    public void Start() =>
        _subscription ??= _realtime.Subscribe<OrderProgressEvent>(HubEvents.OrderReady, e =>
            _notifications.Warning($"TABLE {e.TableCode} — ORDER READY (Ticket {string.Join(", ", e.ReadyTicketNumbers)})"));

    public void Stop()
    {
        _subscription?.Dispose();
        _subscription = null;
    }
}
