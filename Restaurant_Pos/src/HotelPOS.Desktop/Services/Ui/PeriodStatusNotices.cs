using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Desktop.Services.Realtime;

namespace HotelPOS.Desktop.Services.Ui;

// Tells every terminal when a GST period is closed or reopened, wherever the user is: bills dated in a closed
// period can no longer be changed, which a cashier would otherwise only learn from the next refusal.
public sealed class PeriodStatusNotices
{
    private readonly IRealtimeClient _realtime;
    private readonly NoticeCenter _notices;
    private readonly INotificationService _notifications;

    public PeriodStatusNotices(IRealtimeClient realtime, NoticeCenter notices, INotificationService notifications)
    {
        _realtime = realtime;
        _notices = notices;
        _notifications = notifications;
    }

    public IDisposable Start() =>
        _realtime.Subscribe<PeriodStatusChangedEvent>(HubEvents.PeriodStatusChanged, OnChanged);

    private void OnChanged(PeriodStatusChangedEvent e)
    {
        var by = string.IsNullOrWhiteSpace(e.ChangedBy) ? "an administrator" : e.ChangedBy;
        var (text, level) = e.Closed
            ? ($"GST period {e.Label} closed by {by} — bills dated in it can no longer be changed.", NoticeLevel.Warning)
            : ($"GST period {e.Label} reopened by {by} — bills dated in it can change again.", NoticeLevel.Info);
        _notices.Add(new NoticeItem(text, e.ChangedBy, level, DateTime.Now));
        if (e.Closed)
        {
            _notifications.Warning(text);
        }
        else
        {
            _notifications.Info(text);
        }
    }
}
