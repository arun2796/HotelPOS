using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Reports;
using HotelPOS.Contracts.Security;
using HotelPOS.Desktop.Modules.Billing;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Auth;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Realtime;

namespace HotelPOS.Desktop.Modules.Dashboard;

public sealed record KpiTile(string Title, string Value, string Detail, string Glyph, string? Module = null);

public sealed record TopItemRow(int Rank, string Name, string Quantity, string Amount);

public sealed record DayBar(string Label, string Amount, double Height, bool IsToday);

public sealed partial class DashboardViewModel : ObservableObject, INavigationAware, IRefreshable, IDisposable
{
    private static readonly string[] RefreshEvents =
    {
        HubEvents.PaymentCompleted, HubEvents.OrderCreated, HubEvents.BillRequested, HubEvents.KitchenTicketUpdated, HubEvents.OrderCancelled,
    };

    private readonly IReportsApi _api;
    private readonly IRealtimeClient _realtime;
    private readonly INavigationService _navigation;
    private readonly List<IDisposable> _subscriptions = new();
    private CancellationTokenSource? _debounce;
    private CancellationTokenSource? _timer;

    public DashboardViewModel(IReportsApi api, IRealtimeClient realtime, IAuthSession session, INavigationService navigation)
    {
        _api = api;
        _realtime = realtime;
        _navigation = navigation;
        var roles = session.User?.Roles ?? Array.Empty<string>();
        IsManagerView = roles.Contains(Roles.Admin) || roles.Contains(Roles.Manager);
    }

    public bool IsManagerView { get; }

    public ObservableCollection<KpiTile> Tiles { get; } = new();

    public ObservableCollection<TopItemRow> TopItems { get; } = new();

    public ObservableCollection<DayBar> DayBars { get; } = new();

    [ObservableProperty]
    private string _updatedText = string.Empty;

    [ObservableProperty]
    private string _businessDayText = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isBusy;

    public bool HasTopItems => TopItems.Count > 0;

    public int RefreshCount { get; private set; }

    // Events arrive in bursts (one payment fires three of them); the dashboard reloads once, a moment later.
    internal TimeSpan DebounceDelay { get; set; } = TimeSpan.FromSeconds(5);

    public async Task OnNavigatedToAsync(object? parameter)
    {
        foreach (var eventName in RefreshEvents)
        {
            _subscriptions.Add(_realtime.Subscribe<object>(eventName, notice => ScheduleRefresh()));
        }

        await RefreshAsync();
        _timer = new CancellationTokenSource();
        _ = RunTimerAsync(_timer.Token);
    }

    public void OnNavigatedFrom() => Dispose();

    public void Dispose()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
        _debounce?.Cancel();
        _debounce?.Dispose();
        _debounce = null;
        _timer?.Cancel();
        _timer?.Dispose();
        _timer = null;
    }

    public async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            if (IsManagerView)
            {
                await LoadManagerAsync();
            }
            else
            {
                await LoadMineAsync();
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void ScheduleRefresh()
    {
        _debounce?.Cancel();
        _debounce?.Dispose();
        var source = new CancellationTokenSource();
        _debounce = source;
        _ = DelayedRefreshAsync(source.Token);
    }

    [RelayCommand]
    private Task RefreshNowAsync() => RefreshAsync();

    [RelayCommand]
    private Task OpenAsync(KpiTile? tile) => tile?.Module is { } module ? _navigation.NavigateToAsync(module) : Task.CompletedTask;

    private async Task DelayedRefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(DebounceDelay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await RefreshAsync();
    }

    private async Task LoadManagerAsync()
    {
        var result = await _api.GetDashboardAsync();
        if (!result.Success || result.Data is null)
        {
            ErrorMessage = result.IsConnectionFailure ? "Connection unavailable. Figures will refresh when the server is back." : result.Message;
            return;
        }

        var d = result.Data;
        ErrorMessage = null;
        RefreshCount++;
        BusinessDayText = "Business day " + d.BusinessDay.ToString("ddd d MMM", CultureInfo.CurrentCulture);
        UpdatedText = "Updated " + d.GeneratedAtUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);
        Replace(Tiles, new[]
        {
            new KpiTile("Today's sales", Money.Format(d.TodaySales), $"{d.BillsToday} bills settled", "", ModuleRegistry.ClosedBills),
            new KpiTile("Orders today", d.OrdersToday.ToString(CultureInfo.CurrentCulture), $"{d.OpenOrders} still open", "", ModuleRegistry.Tables),
            new KpiTile("Active tables", $"{d.ActiveTables} / {d.TotalTables}", "occupied, ordering or billing", "", ModuleRegistry.Tables),
            new KpiTile("Kitchen tickets", d.PendingKitchenTickets.ToString(CultureInfo.CurrentCulture), "waiting or in preparation", "", ModuleRegistry.KitchenDisplay),
            new KpiTile("Pending bills", d.PendingBills.ToString(CultureInfo.CurrentCulture), "requested or invoiced", "", ModuleRegistry.Billing),
            new KpiTile("Avg ticket time", $"{d.AverageTicketMinutes:0.#} min", "order to ready, today", "", ModuleRegistry.KitchenCompleted),
        });
        Replace(TopItems, d.TopItems.Select((t, i) => new TopItemRow(i + 1, t.ItemName, t.Quantity.ToString(CultureInfo.CurrentCulture), Money.Format(t.Amount))));
        var max = d.LastDays.Count == 0 ? 0m : d.LastDays.Max(p => p.NetSales);
        Replace(DayBars, d.LastDays.Select(p => new DayBar(
            p.Day.ToString("ddd", CultureInfo.CurrentCulture),
            Money.Format(p.NetSales),
            max == 0 ? 0 : (double)(p.NetSales / max),
            p.Day == d.BusinessDay)));
        OnPropertyChanged(nameof(HasTopItems));
    }

    private async Task LoadMineAsync()
    {
        var result = await _api.GetMyDayAsync();
        if (!result.Success || result.Data is null)
        {
            ErrorMessage = result.IsConnectionFailure ? "Connection unavailable. Figures will refresh when the server is back." : result.Message;
            return;
        }

        var d = result.Data;
        ErrorMessage = null;
        RefreshCount++;
        BusinessDayText = "Business day " + d.BusinessDay.ToString("ddd d MMM", CultureInfo.CurrentCulture);
        UpdatedText = "Updated " + d.GeneratedAtUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);
        Replace(Tiles, new[]
        {
            new KpiTile("My orders today", d.OrdersToday.ToString(CultureInfo.CurrentCulture), $"{d.OpenOrders} still open", "", ModuleRegistry.MyOrders),
            new KpiTile("My sales today", Money.Format(d.SalesToday), "settled bills", ""),
            new KpiTile("Tables I am serving", d.TablesServing.ToString(CultureInfo.CurrentCulture), $"{d.CoversToday} guests today", "", ModuleRegistry.Tables),
        });
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }

    private async Task RunTimerAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await RefreshAsync();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
