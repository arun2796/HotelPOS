using System.Globalization;
using HotelPOS.Contracts.Admin;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Realtime;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Shell;

// What the shell does once the hub is back: refresh the page on screen (the billing queue is an IRefreshable page,
// so it is covered when open), reload shell-level data, and leave one bell entry marking the gap. MenuCache and
// ShiftBar subscribe to Reconnected themselves.
public sealed class ReconnectCoordinator : IDisposable
{
    public const string NoticePrefix = "Reconnected — refreshed at ";

    private readonly IRealtimeClient _realtime;
    private readonly NoticeCenter _notices;
    private readonly IUiDispatcher _dispatcher;
    private readonly Func<object?> _currentPage;
    private readonly Func<Task> _reloadShellData;
    private readonly Func<DateTime> _now;
    private NoticeItem? _last;

    public ReconnectCoordinator(
        IRealtimeClient realtime,
        NoticeCenter notices,
        IUiDispatcher dispatcher,
        Func<object?> currentPage,
        Func<Task> reloadShellData,
        Func<DateTime>? now = null)
    {
        _realtime = realtime;
        _notices = notices;
        _dispatcher = dispatcher;
        _currentPage = currentPage;
        _reloadShellData = reloadShellData;
        _now = now ?? (() => DateTime.Now);
        _realtime.Reconnected += OnReconnected;
    }

    public void Dispose() => _realtime.Reconnected -= OnReconnected;

    private void OnReconnected(object? sender, EventArgs e)
    {
        _ = _reloadShellData();
        if (_currentPage() is IRefreshable refreshable)
        {
            _ = refreshable.RefreshAsync();
        }

        var at = _now();
        _dispatcher.Post(() =>
        {
            // One entry only: a repeat replaces the earlier one instead of stacking.
            if (_last is not null && _notices.Items.Remove(_last) && _notices.UnreadCount > 0)
            {
                _notices.UnreadCount--;
            }

            _last = new NoticeItem(NoticePrefix + at.ToString("HH:mm", CultureInfo.CurrentCulture), null, NoticeLevel.Info, at);
            _notices.Add(_last);
        });
    }
}
