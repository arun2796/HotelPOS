using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Devices;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Reports;
using HotelPOS.Contracts.Security;
using HotelPOS.Desktop.Controls;
using HotelPOS.Desktop.Modules.Billing;
using HotelPOS.Desktop.Modules.Common;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Auth;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Realtime;

namespace HotelPOS.Desktop.Modules.Dashboard;

public sealed record KpiTile(string Title, string Value, string Detail, string Glyph, string? Module = null);

// Share: the item's amount relative to the best seller (0..1), drawn as a bar.
public sealed record TopItemRow(int Rank, string Name, string Quantity, string Amount, double Share = 0);

// Manager panels (Phase 20.6): kitchen performance per station, waiters today, terminals online/offline.
public sealed record KitchenStationRow(string Station, string Tickets, string AveragePrep, string LatePercent, bool HasLate);

public sealed record StaffRow(string Name, string Orders, string Sales);

public sealed record DeviceRow(string Name, string Kind, string Status, bool IsOnline);

public sealed record DayBar(string Label, string Amount, double Height, bool IsToday, string CompactAmount = "");

// One payment method in today's split: ColorIndex picks the chart colour (Brush.Chart.N).
public sealed record ChartSlice(string Label, string AmountText, string PercentText, int ColorIndex, Geometry Shape);

public sealed partial class DashboardViewModel : ViewModelBase, INavigationAware, IRefreshable, IDisposable
{
    private static readonly string[] RefreshEvents =
    {
        HubEvents.PaymentCompleted, HubEvents.OrderCreated, HubEvents.BillRequested, HubEvents.KitchenTicketUpdated, HubEvents.OrderCancelled, HubEvents.DeviceStatusChanged,
    };

    private readonly IReportsApi _api;
    private readonly IDevicesApi _devices;
    private readonly IRealtimeClient _realtime;
    private readonly INavigationService _navigation;
    private readonly List<IDisposable> _subscriptions = new();
    private CancellationTokenSource? _debounce;
    private CancellationTokenSource? _timer;

    public DashboardViewModel(IReportsApi api, IRealtimeClient realtime, IAuthSession session, INavigationService navigation, IDevicesApi devices)
    {
        _api = api;
        _realtime = realtime;
        _navigation = navigation;
        _devices = devices;
        var roles = session.User?.Roles ?? Array.Empty<string>();
        IsManagerView = roles.Contains(Roles.Admin) || roles.Contains(Roles.Manager);
        // api/admin/devices is administrator-only, so a manager sees the kitchen and staff panels but not the devices one.
        ShowDevices = roles.Contains(Roles.Admin);
    }

    public bool IsManagerView { get; }

    public bool ShowDevices { get; }

    public ObservableCollection<KitchenStationRow> KitchenStations { get; } = new();

    public ObservableCollection<StaffRow> Staff { get; } = new();

    public ObservableCollection<DeviceRow> Devices { get; } = new();

    [ObservableProperty]
    private string _kitchenAveragePrep = "—";

    [ObservableProperty]
    private string _kitchenLatePercent = "—";

    [ObservableProperty]
    private string _devicesSummary = string.Empty;

    public bool HasKitchen => KitchenStations.Count > 0;

    public bool HasStaff => Staff.Count > 0;

    public bool HasDevices => Devices.Count > 0;

    public ObservableCollection<KpiTile> Tiles { get; } = new();

    public ObservableCollection<TopItemRow> TopItems { get; } = new();

    public ObservableCollection<DayBar> DayBars { get; } = new();

    public ObservableCollection<ChartSlice> PaymentSlices { get; } = new();

    [ObservableProperty]
    private string _paymentTotal = string.Empty;

    public bool HasPayments => PaymentSlices.Count > 0;

    [ObservableProperty]
    private string _updatedText = string.Empty;

    [ObservableProperty]
    private string _businessDayText = string.Empty;

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
        var best = d.TopItems.Count == 0 ? 0m : d.TopItems.Max(t => t.Amount);
        Replace(TopItems, d.TopItems.Select((t, i) => new TopItemRow(i + 1, t.ItemName, t.Quantity.ToString(CultureInfo.CurrentCulture),
            Money.Format(t.Amount), best == 0 ? 0 : (double)(t.Amount / best))));
        var max = d.LastDays.Count == 0 ? 0m : d.LastDays.Max(p => p.NetSales);
        Replace(DayBars, d.LastDays.Select(p => new DayBar(
            p.Day.ToString("ddd", CultureInfo.CurrentCulture),
            Money.Format(p.NetSales),
            max == 0 ? 0 : (double)(p.NetSales / max),
            p.Day == d.BusinessDay,
            p.NetSales == 0 ? string.Empty : Money.Compact(p.NetSales))));
        OnPropertyChanged(nameof(HasTopItems));
        await LoadPaymentSplitAsync(d.BusinessDay);
        await LoadKitchenAsync(d.BusinessDay);
        await LoadStaffAsync(d.BusinessDay);
        await LoadDevicesAsync();
    }

    // The three panels below are optional: each keeps the dashboard intact when its endpoint is unavailable.
    private async Task LoadKitchenAsync(DateOnly day)
    {
        var result = await _api.GetRowsAsync<KitchenPerformanceRowDto>(ReportKeys.Kitchen, day, day);
        var rows = result is { Success: true, Data: not null } ? result.Data.Where(r => r.Tickets > 0).ToList() : new List<KitchenPerformanceRowDto>();
        var tickets = rows.Sum(r => r.Tickets);
        KitchenAveragePrep = tickets == 0 ? "—" : $"{rows.Sum(r => r.AveragePrepMinutes * r.Tickets) / tickets:0.#} min";
        KitchenLatePercent = tickets == 0 ? "—" : $"{(decimal)rows.Sum(r => r.LateTickets) * 100 / tickets:0.#}%";
        Replace(KitchenStations, rows.Select(r => new KitchenStationRow(r.Station, r.Tickets.ToString(CultureInfo.CurrentCulture),
            $"{r.AveragePrepMinutes:0.#} min", $"{r.LatePercent:0.#}%", r.LateTickets > 0)));
        OnPropertyChanged(nameof(HasKitchen));
    }

    private async Task LoadStaffAsync(DateOnly day)
    {
        var result = await _api.GetRowsAsync<WaiterPerformanceRowDto>(ReportKeys.Waiters, day, day);
        var rows = result is { Success: true, Data: not null } ? result.Data.OrderByDescending(r => r.NetSales).Take(6) : Enumerable.Empty<WaiterPerformanceRowDto>();
        Replace(Staff, rows.Select(r => new StaffRow(r.WaiterName, r.Orders.ToString(CultureInfo.CurrentCulture), Money.Format(r.NetSales))));
        OnPropertyChanged(nameof(HasStaff));
    }

    private async Task LoadDevicesAsync()
    {
        if (!ShowDevices)
        {
            return;
        }

        var result = await _devices.ListAsync();
        var active = result is { Success: true, Data: not null } ? result.Data.Where(x => x.IsActive).ToList() : new List<DeviceDto>();
        var online = active.Count(x => x.IsOnline);
        DevicesSummary = active.Count == 0 ? "No terminals registered" : $"{online} online · {active.Count - online} offline";
        Replace(Devices, active.OrderBy(x => x.IsOnline).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Select(x => new DeviceRow(
            x.Name, x.Type.ToString(), x.IsOnline ? "Online" : x.LastSeenAtUtc is { } seen ? "Seen " + seen.ToLocalTime().ToString("dd MMM HH:mm", CultureInfo.CurrentCulture) : "Never seen", x.IsOnline)));
        OnPropertyChanged(nameof(HasDevices));
    }

    [RelayCommand]
    private Task OpenAuditAsync() => _navigation.NavigateToAsync(ModuleRegistry.Audit);

    // Today's net takings per payment method, from the payments report (no extra endpoint). Optional: the rest of
    // the dashboard stays when it cannot load.
    private async Task LoadPaymentSplitAsync(DateOnly day)
    {
        var result = await _api.GetRowsAsync<PaymentSummaryRowDto>(ReportKeys.Payments, day, day);
        var rows = result is { Success: true, Data: not null }
            ? result.Data.Where(r => r.Net > 0).OrderByDescending(r => r.Net).ToList()
            : new List<PaymentSummaryRowDto>();
        var total = rows.Sum(r => r.Net);
        var slices = new List<ChartSlice>();
        var angle = 0d;
        foreach (var (row, index) in rows.Select((r, i) => (r, i)))
        {
            var sweep = (double)(row.Net / total) * 360;
            slices.Add(new ChartSlice(row.Method, Money.Format(row.Net), $"{row.Net / total * 100:0}%", index % ChartColours,
                ChartGeometry.RingSegment(angle, sweep, 72, 20)));
            angle += sweep;
        }

        Replace(PaymentSlices, slices);
        PaymentTotal = Money.Compact(total);
        OnPropertyChanged(nameof(HasPayments));
    }

    private const int ChartColours = 6;

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
