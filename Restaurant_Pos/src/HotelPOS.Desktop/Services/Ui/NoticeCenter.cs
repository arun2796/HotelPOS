using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HotelPOS.Contracts.Admin;

namespace HotelPOS.Desktop.Services.Ui;

public sealed record NoticeItem(string Message, string? SentBy, NoticeLevel Level, DateTime ReceivedAt)
{
    public string SentByText => string.IsNullOrWhiteSpace(SentBy) ? ReceivedAt.ToString("HH:mm") : $"{SentBy} · {ReceivedAt:HH:mm}";
}

// Server notices received since sign-in, for the bell in the top bar. A toast shows each notice once; this list
// keeps them so a notice missed while away from the terminal can still be read.
public sealed partial class NoticeCenter : ObservableObject
{
    private const int MaxItems = 50;
    private readonly IUiDispatcher _dispatcher;

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

    public void Add(NoticeItem notice) => _dispatcher.Post(() =>
    {
        Items.Insert(0, notice);
        while (Items.Count > MaxItems)
        {
            Items.RemoveAt(Items.Count - 1);
        }

        UnreadCount++;
        OnPropertyChanged(nameof(IsEmpty));
    });

    public void MarkAllRead() => UnreadCount = 0;

    public void Clear() => _dispatcher.Post(() =>
    {
        Items.Clear();
        UnreadCount = 0;
        OnPropertyChanged(nameof(IsEmpty));
    });
}
