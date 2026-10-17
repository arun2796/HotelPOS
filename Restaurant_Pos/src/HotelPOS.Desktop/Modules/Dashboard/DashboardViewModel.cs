using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Devices;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Orders;
using HotelPOS.Contracts.Presentation;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Reports;
using HotelPOS.Contracts.Security;
using HotelPOS.Desktop.Controls;
using HotelPOS.Desktop.Modules.Billing;
using HotelPOS.Desktop.Modules.Common;
using HotelPOS.Desktop.Modules.Orders;
using HotelPOS.Desktop.Modules.Tables;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Auth;
using HotelPOS.Desktop.Services.Menu;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Realtime;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Modules.Dashboard;

// The Phase 20 tile shape, still used by the cashier home (Modules/Cash); the admin dashboard moved to KpiCard below.
public sealed record KpiTile(string Title, string Value, string Detail, string Glyph, string? Module = null);

// One KPI card (Phase 21 A2): icon + tone, the figure, a caption, an optional change versus yesterday (only when the
// data is real) and an optional progress (occupancy). Module/Parameter is where a click goes (a report key, a module).
public sealed record KpiCard(string Title, string Value, string Detail, string Icon, StatusTone Tone,
    string? Module = null, object? Parameter = null, double? Delta = null, double? Progress = null)
{
    public bool HasProgress => Progress is not null;

    /// <summary>For the bar binding (a nullable would be a binding error on a double); 0 when the card has no progress.</summary>
    public double ProgressValue => Progress ?? 0;
}

// Share: the item's amount relative to the best seller (0..1), drawn as a bar. ImageFile: the cached picture (D11), else initials.
public sealed record TopItemRow(int Rank, string Name, string Quantity, string Amount, double Share = 0, string? ImageFile = null)
{
    public string Initials => Dashboard.Initials.Of(Name);
}

// Manager panels (Phase 20.6): kitchen performance per station, waiters today, terminals online/offline.
public sealed record KitchenStationRow(string Station, string Tickets, string AveragePrep, string LatePercent, bool HasLate);

public sealed record StaffRow(string Name, string Orders, string Sales)
{
    public string Initials => Dashboard.Initials.Of(Name);
}

public sealed record DeviceRow(string Name, string Kind, string Status, bool IsOnline)
{
    public StatusDisplay Display => IsOnline ? new StatusDisplay("Online", StatusTone.Success, "signal") : new StatusDisplay("Offline", StatusTone.Danger, "wifi-off");
}

public sealed record DayBar(string Label, string Amount, double Height, bool IsToday, string CompactAmount = "");

// One payment method in today's split: ColorIndex picks the chart colour (Brush.Chart.N).
public sealed record ChartSlice(string Label, string AmountText, string PercentText, int ColorIndex, Geometry Shape);

// One status in the Live Order Status donut (A3): the badge for the legend, how many orders, the share and the ring segment.
public sealed record StatusSlice(StatusDisplay Display, int Count, string PercentText, int ColorIndex, Geometry Shape)
{
    public string CountText => Count.ToString(CultureInfo.CurrentCulture);
}

// Table status summary (A5): one count per table status that occurs on the floor.
public sealed record TableStatusCount(StatusDisplay Display, int Count)
{
    public string CountText => Count.ToString(CultureInfo.CurrentCulture);
}

// Recent activity (A6): an audit-log row or a live notice as one readable line.
public sealed record ActivityRow(string TimeText, string Text, StatusTone Tone, bool IsLive)
{
    public string DotBrushKey => StatusLabels.SolidBrushKey(Tone);
}

internal static class Initials
{
    public static string Of(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(p => char.IsLetterOrDigit(p[0])).ToList();
        return parts.Count switch
        {
            0 => "?",
            1 => parts[0].Length > 1 ? parts[0][..2].ToUpperInvariant() : parts[0].ToUpperInvariant(),
            _ => (parts[0][..1] + parts[1][..1]).ToUpperInvariant(),
        };
    }
}

public sealed partial class DashboardViewModel : ViewModelBase, INavigationAware, IRefreshable, IDisposable
{
    public const int OrdersPageSize = 10;
    public const int MaxTableCards = 8;
    public const int MaxActivity = 8;
    private const int ChartColours = 6;

    private static readonly string[] RefreshEvents =
    {
        HubEvents.PaymentCompleted, HubEvents.OrderCreated, HubEvents.OrderUpdated, HubEvents.OrderCancelled, HubEvents.OrderReady,
        HubEvents.OrderServed, HubEvents.BillRequested, HubEvents.KitchenTicketUpdated, HubEvents.TableStatusChanged, HubEvents.DeviceStatusChanged,
    };

    private readonly IReportsApi _api;
    private readonly IDevicesApi _devices;
    private readonly IOrdersApi _orders;
    private readonly IFloorApi _floor;
    private readonly IAuditApi _audit;
    private readonly NoticeCenter _notices;
    private readonly IMenuCache _menuCache;
    private readonly IRealtimeClient _realtime;
    private readonly INavigationService _navigation;
    private readonly List<IDisposable> _subscriptions = new();
    private List<OrderSummaryDto> _activeOrders = new();
    private CancellationTokenSource? _debounce;
    private CancellationTokenSource? _timer;
    private bool _rebuildingOrders;
    private bool _listening;

    public DashboardViewModel(
        IReportsApi api,
        IRealtimeClient realtime,
        IAuthSession session,
        INavigationService navigation,
        IDevicesApi devices,
        IOrdersApi orders,
        IFloorApi floor,
        IAuditApi audit,
        NoticeCenter notices,
        IMenuCache menuCache)
    {
        _api = api;
        _realtime = realtime;
        _navigation = navigation;
        _devices = devices;
        _orders = orders;
        _floor = floor;
        _audit = audit;
        _notices = notices;
        _menuCache = menuCache;
        var user = session.User;
        var roles = user?.Roles ?? Array.Empty<string>();
        IsManagerView = roles.Contains(Roles.Admin) || roles.Contains(Roles.Manager);
        // api/admin/devices is administrator-only, so a manager sees the kitchen and staff panels but not the devices one.
        ShowDevices = roles.Contains(Roles.Admin);
        // D10: the audit log needs AuditView; without it Recent activity falls back to this session's bell feed.
        HasAuditAccess = roles.Contains(Roles.Admin) || (user?.Permissions.Contains(Permissions.AuditView) ?? false);
        WelcomeText = string.IsNullOrWhiteSpace(user?.DisplayName) ? "Welcome back" : $"Welcome back, {user!.DisplayName}";
        OrderFilters.Add(new OrderStatusFilter(null, "All", 0));
        _selectedOrderFilter = OrderFilters[0];
        _connectionDisplay = ConnectionDisplayFor(realtime.Status);
    }

    public bool IsManagerView { get; }

    public bool ShowDevices { get; }

    public bool HasAuditAccess { get; }

    public string WelcomeText { get; }

    // ----- Header (A2)

    [ObservableProperty]
    private StatusDisplay _connectionDisplay;

    [ObservableProperty]
    private string _updatedText = string.Empty;

    [ObservableProperty]
    private string _businessDayText = string.Empty;

    // ----- KPI cards (A2)

    public ObservableCollection<KpiCard> Cards { get; } = new();

    // ----- Live order status (A3) and the active orders table (A4)

    public ObservableCollection<StatusSlice> OrderStatusSlices { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveOrders))]
    private int _activeOrderCount;

    public bool HasActiveOrders => ActiveOrderCount > 0;

    public ObservableCollection<OrderStatusFilter> OrderFilters { get; } = new();

    public IReadOnlyList<SortOption> OrderSortOptions => ActiveOrderQueries.SortOptions;

    public ObservableCollection<OrderRow> ActiveOrderRows { get; } = new();

    [ObservableProperty]
    private OrderStatusFilter? _selectedOrderFilter;

    [ObservableProperty]
    private string _orderSearch = string.Empty;

    [ObservableProperty]
    private SortOption _selectedOrderSort = ActiveOrderQueries.SortOptions[0];

    [ObservableProperty]
    private int _orderPageIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFilteredOrders))]
    private int _filteredOrderCount;

    public bool HasFilteredOrders => FilteredOrderCount > 0;

    [ObservableProperty]
    private string? _ordersError;

    // ----- Table status (A5)

    public ObservableCollection<TableTileViewModel> TableTiles { get; } = new();

    public ObservableCollection<TableStatusCount> TableStatusCounts { get; } = new();

    [ObservableProperty]
    private string _moreTablesText = string.Empty;

    [ObservableProperty]
    private string _tablesSummary = string.Empty;

    public bool HasTables => TableTiles.Count > 0;

    // ----- Recent activity (A6)

    public ObservableCollection<ActivityRow> Activity { get; } = new();

    public bool HasActivity => Activity.Count > 0;

    public string ActivitySourceText => HasAuditAccess ? "From the audit log, live events on top." : "Live events of this session (the audit log needs the Audit permission).";

    // ----- Phase 20.6 panels, kept: kitchen performance, staff today, terminals

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

    // ----- Charts kept from Phase 20: 7-day bars, payments donut, top items (now with thumbnails, D11)

    public ObservableCollection<TopItemRow> TopItems { get; } = new();

    public ObservableCollection<DayBar> DayBars { get; } = new();

    public ObservableCollection<ChartSlice> PaymentSlices { get; } = new();

    [ObservableProperty]
    private string _paymentTotal = string.Empty;

    public bool HasPayments => PaymentSlices.Count > 0;

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

        _realtime.StatusChanged += OnConnectionChanged;
        if (!_listening)
        {
            _notices.Items.CollectionChanged += OnNoticesChanged;
            _listening = true;
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
        _realtime.StatusChanged -= OnConnectionChanged;
        if (_listening)
        {
            _notices.Items.CollectionChanged -= OnNoticesChanged;
            _listening = false;
        }

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

    // ----- Derivations (pure, tested)

    /// <summary>Percent change of today against yesterday; null when yesterday is unknown or zero (no invented comparison).</summary>
    public static double? SalesDelta(decimal today, decimal? yesterday) =>
        yesterday is { } y && y > 0 ? (double)((today - y) / y * 100) : null;

    /// <summary>Sales ÷ settled bills; null until a bill is settled.</summary>
    public static decimal? AverageOrderValue(decimal sales, int bills) => bills > 0 ? sales / bills : null;

    public static double Occupancy(int active, int total) => total <= 0 ? 0 : Math.Clamp((double)active / total, 0, 1);

    public static StatusDisplay ConnectionDisplayFor(ConnectionStatus status) => status switch
    {
        ConnectionStatus.Connected => new StatusDisplay("Online", StatusTone.Success, "signal"),
        ConnectionStatus.Connecting or ConnectionStatus.Reconnecting => StatusPresentation.Reconnecting,
        _ => StatusPresentation.Offline,
    };

    // ----- Commands

    [RelayCommand]
    private Task RefreshNowAsync() => RefreshAsync();

    [RelayCommand]
    private Task OpenAsync(KpiCard? card) => card?.Module is { } module ? _navigation.NavigateToAsync(module, card.Parameter) : Task.CompletedTask;

    [RelayCommand]
    private Task OpenAuditAsync() => _navigation.NavigateToAsync(ModuleRegistry.Audit);

    [RelayCommand]
    private Task OpenOrdersAsync() => _navigation.NavigateToAsync(ModuleRegistry.Orders);

    [RelayCommand]
    private Task OpenFloorPlanAsync() => _navigation.NavigateToAsync(ModuleRegistry.Tables);

    /// <summary>"View" on an order row: the table behind it, the screen that already knows how to act on the order.</summary>
    [RelayCommand]
    private Task ViewOrderAsync(OrderRow? row) => row is null ? Task.CompletedTask : _navigation.NavigateToAsync(ModuleRegistry.Tables, row.Order.TableId);

    [RelayCommand]
    private Task OpenTableAsync(TableTileViewModel? tile) => tile is null ? Task.CompletedTask : _navigation.NavigateToAsync(ModuleRegistry.Tables, tile.Id);

    [RelayCommand]
    private Task OpenReportAsync(string? key) => _navigation.NavigateToAsync(ModuleRegistry.Reports, key);

    [RelayCommand]
    private void OrderPageChanged(int pageIndex) => ApplyOrderFilters();

    // ----- Loading

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
        Replace(Cards, BuildCards(d));
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

        // Each panel below is optional: the dashboard stays intact when one endpoint is unavailable.
        await LoadActiveOrdersAsync();
        await LoadTablesAsync();
        await LoadActivityAsync();
        await LoadPaymentSplitAsync(d.BusinessDay);
        await LoadKitchenAsync(d.BusinessDay);
        await LoadStaffAsync(d.BusinessDay);
        await LoadDevicesAsync();
        await LoadThumbnailsAsync();
    }

    /// <summary>The four cards (A2): sales with a real delta only when yesterday's figure exists, orders, derived AOV, occupancy.</summary>
    public static IReadOnlyList<KpiCard> BuildCards(DashboardDto d)
    {
        var yesterday = d.LastDays.FirstOrDefault(p => p.Day == d.BusinessDay.AddDays(-1))?.NetSales;
        var delta = SalesDelta(d.TodaySales, yesterday);
        var salesDetail = delta is null
            ? Plural.Count(d.BillsToday, "bill") + " settled"
            : $"vs {Money.Format(yesterday!.Value)} yesterday";
        var aov = AverageOrderValue(d.TodaySales, d.BillsToday);
        var occupancy = Occupancy(d.ActiveTables, d.TotalTables);
        return new[]
        {
            new KpiCard("Today's sales", Money.Format(d.TodaySales), salesDetail, "receipt", StatusTone.Success,
                ModuleRegistry.Reports, ReportKeys.DailySales, delta),
            new KpiCard("Orders today", d.OrdersToday.ToString(CultureInfo.CurrentCulture), $"{d.OpenOrders} open now", "utensils", StatusTone.Info,
                ModuleRegistry.Orders),
            new KpiCard("Average order value", aov is { } value ? Money.Format(value) : "—",
                aov is null ? "no bills settled yet" : $"sales ÷ {Plural.Count(d.BillsToday, "bill")} settled", "star", StatusTone.Info,
                ModuleRegistry.Reports, ReportKeys.DailySales),
            new KpiCard("Occupied tables", $"{d.ActiveTables} / {d.TotalTables}", $"{occupancy * 100:0}% occupancy", "table", StatusTone.Warning,
                ModuleRegistry.Tables, Progress: occupancy),
        };
    }

    private async Task LoadActiveOrdersAsync()
    {
        var result = await _orders.GetActiveAsync();
        if (!result.Success || result.Data is null)
        {
            OrdersError = result.IsConnectionFailure ? "Connection unavailable." : result.Message;
            return;
        }

        OrdersError = null;
        _activeOrders = result.Data;
        ActiveOrderCount = _activeOrders.Count;
        BuildOrderStatusSlices();
        RebuildOrderFilters();
        ApplyOrderFilters();
    }

    private void BuildOrderStatusSlices()
    {
        var counts = ActiveOrderQueries.CountByStatus(_activeOrders);
        var total = counts.Sum(c => c.Count);
        var slices = new List<StatusSlice>();
        var angle = 0d;
        foreach (var ((status, count), index) in counts.Select((c, i) => (c, i)))
        {
            var sweep = total == 0 ? 0 : (double)count / total * 360;
            slices.Add(new StatusSlice(StatusPresentation.For(status), count, $"{(total == 0 ? 0 : count * 100.0 / total):0}%", index % ChartColours,
                ChartGeometry.RingSegment(angle, sweep, 72, 20)));
            angle += sweep;
        }

        Replace(OrderStatusSlices, slices);
    }

    partial void OnSelectedOrderFilterChanged(OrderStatusFilter? value)
    {
        if (!_rebuildingOrders)
        {
            OrderPageIndex = 0;
            ApplyOrderFilters();
        }
    }

    partial void OnOrderSearchChanged(string value)
    {
        OrderPageIndex = 0;
        ApplyOrderFilters();
    }

    partial void OnSelectedOrderSortChanged(SortOption value) => ApplyOrderFilters();

    private void RebuildOrderFilters()
    {
        var current = SelectedOrderFilter?.Status;
        OrderFilters.Clear();
        OrderFilters.Add(new OrderStatusFilter(null, "All", _activeOrders.Count));
        foreach (var (status, count) in ActiveOrderQueries.CountByStatus(_activeOrders))
        {
            OrderFilters.Add(new OrderStatusFilter(status, StatusLabels.For(status), count));
        }

        _rebuildingOrders = true;
        try
        {
            SelectedOrderFilter = OrderFilters.FirstOrDefault(f => f.Status == current) ?? OrderFilters[0];
        }
        finally
        {
            _rebuildingOrders = false;
        }
    }

    private void ApplyOrderFilters()
    {
        var now = DateTime.UtcNow;
        var visible = ActiveOrderQueries.Apply(_activeOrders, SelectedOrderFilter?.Status, OrderSearch, SelectedOrderSort.Sort);
        FilteredOrderCount = visible.Count;
        var lastPage = Math.Max(0, (visible.Count - 1) / OrdersPageSize);
        if (OrderPageIndex > lastPage)
        {
            OrderPageIndex = lastPage;
        }

        Replace(ActiveOrderRows, ActiveOrderQueries.Page(visible, OrderPageIndex, OrdersPageSize)
            .Select(o => new OrderRow(o, ActiveOrderQueries.Elapsed(ActiveOrderQueries.Since(o), now), Money.Format(o.ApproxTotal))));
    }

    private async Task LoadTablesAsync()
    {
        var result = await _floor.GetMapAsync();
        if (!result.Success || result.Data is null)
        {
            return;
        }

        var map = result.Data;
        var tables = map.Sections.Where(s => s.IsActive).OrderBy(s => s.SortOrder)
            .SelectMany(s => s.Tables.Where(t => t.IsActive).OrderBy(t => t.Code, StringComparer.OrdinalIgnoreCase))
            .ToList();
        Replace(TableStatusCounts, tables.GroupBy(t => t.Status).OrderBy(g => (int)g.Key)
            .Select(g => new TableStatusCount(StatusPresentation.For(g.Key), g.Count())));
        // Attention first: tables with a live order before free ones, so the eight cards show what needs a manager.
        var shown = tables.OrderBy(t => t.Status is TableStatus.Available or TableStatus.OutOfService ? 1 : 0).Take(MaxTableCards).ToList();
        Replace(TableTiles, shown.Select(t =>
        {
            var tile = new TableTileViewModel(t, map.ServerTimeUtc);
            tile.UpdateElapsed(map.ServerTimeUtc);
            return tile;
        }));
        var more = tables.Count - shown.Count;
        MoreTablesText = more > 0 ? $"+{Plural.Count(more, "more table")} on the floor plan" : string.Empty;
        TablesSummary = Plural.Count(tables.Count, "table") + " · " + tables.Count(t => t.Status is not (TableStatus.Available or TableStatus.OutOfService)).ToString(CultureInfo.CurrentCulture) + " occupied";
        OnPropertyChanged(nameof(HasTables));
    }

    private async Task LoadActivityAsync()
    {
        if (!HasAuditAccess)
        {
            Replace(Activity, _notices.Items.Take(MaxActivity).Select(FromNotice));
            OnPropertyChanged(nameof(HasActivity));
            return;
        }

        var result = await _audit.QueryAsync(new AuditLogQuery { Page = 1, PageSize = MaxActivity });
        var rows = result is { Success: true, Data.Items: not null }
            ? result.Data.Items.OrderByDescending(r => r.TimestampUtc).Take(MaxActivity).Select(r =>
                new ActivityRow(AuditSentences.TimeText(r.TimestampUtc), AuditSentences.Describe(r), AuditSentences.ToneFor(r.Action), IsLive: false))
            : _notices.Items.Take(MaxActivity).Select(FromNotice);
        Replace(Activity, rows);
        OnPropertyChanged(nameof(HasActivity));
    }

    private static ActivityRow FromNotice(NoticeItem notice) => new(
        notice.ReceivedAt.ToString("HH:mm", CultureInfo.CurrentCulture),
        string.IsNullOrWhiteSpace(notice.SentBy) ? notice.Message : $"{notice.SentBy}: {notice.Message}",
        notice.Level switch { NoticeLevel.Critical => StatusTone.Danger, NoticeLevel.Warning => StatusTone.Warning, _ => StatusTone.Info },
        IsLive: true);

    // A notice that arrives while the dashboard is open goes on top of the activity list (D10), the oldest row drops off.
    private void OnNoticesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || e.NewItems is null)
        {
            return;
        }

        foreach (var item in e.NewItems.OfType<NoticeItem>().Reverse())
        {
            Activity.Insert(0, FromNotice(item));
        }

        while (Activity.Count > MaxActivity)
        {
            Activity.RemoveAt(Activity.Count - 1);
        }

        OnPropertyChanged(nameof(HasActivity));
    }

    private void OnConnectionChanged(object? sender, ConnectionStatus status) => ConnectionDisplay = ConnectionDisplayFor(status);

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

    // D11: the top items' pictures come from the menu already cached on this terminal (ImageUrl), matched by name; an
    // item without a picture keeps its initials. Nothing is fetched from a new endpoint.
    private async Task LoadThumbnailsAsync()
    {
        var entries = _menuCache.Menu?.Items;
        if (entries is null || TopItems.Count == 0)
        {
            return;
        }

        for (var i = 0; i < TopItems.Count; i++)
        {
            var row = TopItems[i];
            var url = entries.FirstOrDefault(e => string.Equals(e.Name, row.Name, StringComparison.OrdinalIgnoreCase))?.ImageUrl;
            if (string.IsNullOrEmpty(url))
            {
                continue;
            }

            var file = await _menuCache.GetImageFileAsync(url);
            if (file is not null && i < TopItems.Count && ReferenceEquals(TopItems[i], row))
            {
                TopItems[i] = row with { ImageFile = file };
            }
        }
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
        Replace(Cards, new[]
        {
            new KpiCard("My orders today", d.OrdersToday.ToString(CultureInfo.CurrentCulture), $"{d.OpenOrders} still open", "utensils", StatusTone.Info, ModuleRegistry.MyOrders),
            new KpiCard("My sales today", Money.Format(d.SalesToday), "settled bills", "receipt", StatusTone.Success),
            new KpiCard("Tables I am serving", d.TablesServing.ToString(CultureInfo.CurrentCulture), $"{d.CoversToday} guests today", "table", StatusTone.Warning, ModuleRegistry.Tables),
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
