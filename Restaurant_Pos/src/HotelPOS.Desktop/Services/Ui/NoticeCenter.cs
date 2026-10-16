using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Realtime;

namespace HotelPOS.Desktop.Services.Ui;

// One entry in the bell. Read state and the repeat count change after it is added, so it is observable.
public sealed partial class NoticeItem : ObservableObject
{
    public NoticeItem(string message, string? sentBy, NoticeLevel level, DateTime receivedAt)
    {
        Message = message;
        SentBy = sentBy;
        Level = level;
        ReceivedAt = receivedAt;
    }

    public Guid Id { get; } = Guid.NewGuid();

    public NotificationKind? Kind { get; init; }

    public NotificationTarget? Target { get; init; }

    // Repeats of the same event (same key within 30 seconds) update this entry instead of adding another.
    public string? Key { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SentByText))]
    private string _message = string.Empty;

    [ObservableProperty]
    private NoticeLevel _level;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SentByText))]
    private DateTime _receivedAt;

    [ObservableProperty]
    private bool _isRead;

    [ObservableProperty]
    private bool _isAcknowledged;

    public string? SentBy { get; }

    public bool CanOpen => Target is not null;

    public string SentByText => string.IsNullOrWhiteSpace(SentBy) ? ReceivedAt.ToString("HH:mm") : $"{SentBy} · {ReceivedAt:HH:mm}";

    // Segoe MDL2 Assets glyph for the kind of notice.
    public string Glyph => Kind switch
    {
        NotificationKind.TicketCreated => "",
        NotificationKind.OrderAccepted => "",
        NotificationKind.OrderPreparing or NotificationKind.TicketDelayed => "",
        NotificationKind.OrderReady => "",
        NotificationKind.BillRequested => "",
        NotificationKind.BillClaimed => "",
        NotificationKind.PaymentCompleted => "",
        NotificationKind.DeviceOnline or NotificationKind.DeviceOffline => "",
        NotificationKind.PeriodChanged => "",
        NotificationKind.PrintFailed => "",
        NotificationKind.DayEndWarning => "",
        NotificationKind.PaymentFailed => "",
        _ => "",
    };

    public string LevelText => Level switch
    {
        NoticeLevel.Critical => "Critical",
        NoticeLevel.Warning => "Warning",
        _ => "Info",
    };

    partial void OnLevelChanged(NoticeLevel value) => OnPropertyChanged(nameof(LevelText));
}

// Notices received since sign-in, for the bell in the top bar. A toast shows each notice once; this list
// keeps them so a notice missed while away from the terminal can still be read, acknowledged or opened.
public sealed partial class NoticeCenter : ObservableObject
{
    private const int MaxItems = 50;
    private static readonly TimeSpan DedupWindow = TimeSpan.FromSeconds(30);
    private readonly IUiDispatcher _dispatcher;
    private readonly Dictionary<string, DateTime> _recent = new();

    public NoticeCenter(IUiDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public ObservableCollection<NoticeItem> Items { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnread))]
    private int _unreadCount;

    public bool HasUnread => UnreadCount > 0;

    public bool IsEmpty => Items.Count == 0;

    // Returns true when a new entry was added, false when a repeat updated the existing one (no second toast).
    public bool Add(NoticeItem notice)
    {
        // Decided here, on the calling thread, so the caller knows at once whether to toast; the list itself is
        // only touched on the UI thread.
        bool repeat;
        lock (_recent)
        {
            repeat = notice.Key is not null
                && _recent.TryGetValue(notice.Key, out var last)
                && notice.ReceivedAt - last <= DedupWindow;
            if (notice.Key is not null)
            {
                _recent[notice.Key] = notice.ReceivedAt;
            }
        }

        _dispatcher.Post(() =>
        {
            var existing = repeat ? Items.FirstOrDefault(i => i.Key == notice.Key) : null;
            if (existing is not null)
            {
                existing.Message = notice.Message;
                existing.Level = notice.Level;
                existing.ReceivedAt = notice.ReceivedAt;
                existing.IsRead = false;
                existing.IsAcknowledged = false;
                Items.Move(Items.IndexOf(existing), 0);
            }
            else
            {
                Items.Insert(0, notice);
                while (Items.Count > MaxItems)
                {
                    Items.RemoveAt(Items.Count - 1);
                }
            }

            Recount();
            OnPropertyChanged(nameof(IsEmpty));
        });
        return !repeat;
    }

    public void MarkRead(Guid id)
    {
        var item = Items.FirstOrDefault(i => i.Id == id);
        if (item is not null)
        {
            item.IsRead = true;
            Recount();
        }
    }

    // Read and dealt with: the entry stays in the list but is greyed and offers no Acknowledge button.
    public void Acknowledge(Guid id)
    {
        var item = Items.FirstOrDefault(i => i.Id == id);
        if (item is not null)
        {
            item.IsRead = true;
            item.IsAcknowledged = true;
            Recount();
        }
    }

    public void MarkAllRead()
    {
        foreach (var item in Items)
        {
            item.IsRead = true;
        }

        Recount();
    }

    public void Clear() => _dispatcher.Post(() =>
    {
        lock (_recent)
        {
            _recent.Clear();
        }

        Items.Clear();
        UnreadCount = 0;
        OnPropertyChanged(nameof(IsEmpty));
    });

    private void Recount() => UnreadCount = Items.Count(i => !i.IsRead);
}
