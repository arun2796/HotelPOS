using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using HotelPOS.Desktop.Modules.Common;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Auth;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Realtime;

namespace HotelPOS.Desktop.Modules.Kitchen;

// One counted item on the stock dashboard.
public sealed class StockRowViewModel
{
    public StockRowViewModel(StockOverviewRowDto row)
    {
        Row = row;
    }

    public StockOverviewRowDto Row { get; }

    public string ItemName => Row.ItemName;
    public string Category => Row.Category;
    public string Station => Row.Station;
    public int Opening => Row.Opening;
    public int Reserved => Row.Reserved;
    public int Sold => Row.Sold;
    public int Refilled => Row.Refilled;
    public int Wasted => Row.Wasted;
    public int Available => Row.Available;
    public string LowAtText => (Row.ReorderLevel ?? StockStatusRules.DefaultLowLevel).ToString(CultureInfo.CurrentCulture);
    public string StatusText => StockStatusRules.Text(Row.Status) + (Row.Mode == StockTrackingMode.Approximate ? " (approx.)" : string.Empty);
    public bool IsLow => Row.Status == StockStatus.Low;
    public bool IsOut => Row.Status is StockStatus.Out or StockStatus.Unavailable;

    public string UpdatedText => Row.LastUpdatedUtc is { } at
        ? at.ToLocalTime().ToString("dd MMM HH:mm", CultureInfo.CurrentCulture) + (Row.UpdatedBy is { Length: > 0 } by ? " · " + by : string.Empty)
        : string.Empty;
}

/// <summary>
/// The kitchen stock dashboard for Admin, Manager and Kitchen: what was prepared, reserved, sold, refilled and wasted,
/// what is left and its status, and (with the reports permission) the day's stock movements by type. It follows the live
/// MenuChanged event and reloads everything after a reconnect, so every screen shows the same numbers.
/// </summary>
public sealed partial class KitchenStockViewModel : ViewModelBase, INavigationAware, IRefreshable, IDisposable
{
    public const string AllFilter = "All";
    public const string LowFilter = "Low stock";
    public const string OutFilter = "Out of stock";

    private static readonly (string Label, StockMovementType? Type)[] MovementKinds =
    {
        ("All movements", null), ("Waste", StockMovementType.Waste), ("Corrections", StockMovementType.Adjustment),
        ("Refills", StockMovementType.Refill), ("Orders", StockMovementType.OrderReservation), ("Returned", StockMovementType.OrderRelease),
    };

    private readonly IMenuApi _menuApi;
    private readonly IRealtimeClient _realtime;
    private readonly List<IDisposable> _subscriptions = new();
    private List<StockRowViewModel> _all = new();
    private int _refreshing;

    public KitchenStockViewModel(IMenuApi menuApi, IRealtimeClient realtime, IAuthSession session)
    {
        _menuApi = menuApi;
        _realtime = realtime;
        var permissions = session.User?.Permissions ?? Array.Empty<string>();
        CanSeeMovements = permissions.Contains(Permissions.ReportsView);
        _selectedFilter = AllFilter;
        _selectedMovementKind = MovementKinds[0].Label;
        _connectionText = ConnectionLabel(realtime.Status);
    }

    public IReadOnlyList<string> Filters { get; } = new[] { AllFilter, LowFilter, OutFilter };

    public IReadOnlyList<string> MovementFilters { get; } = MovementKinds.Select(k => k.Label).ToList();

    public ObservableCollection<StockRowViewModel> Rows { get; } = new();

    public ObservableCollection<StockHistoryRow> Movements { get; } = new();

    public bool CanSeeMovements { get; }

    [ObservableProperty]
    private string _selectedFilter;

    [ObservableProperty]
    private string _selectedMovementKind;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowStock))]
    private bool _showMovements;

    public bool ShowStock => !ShowMovements;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    private string _lastSyncedText = "Not synced yet";

    [ObservableProperty]
    private string _connectionText;

    public bool IsEmpty => Rows.Count == 0 && !IsBusy;

    public async Task OnNavigatedToAsync(object? parameter)
    {
        _subscriptions.Add(_realtime.Subscribe<MenuChangedEvent>(HubEvents.MenuChanged, notice => _ = RefreshAsync()));
        _realtime.Reconnected += OnReconnected;
        _realtime.StatusChanged += OnStatusChanged;
        await RefreshAsync();
    }

    public void OnNavigatedFrom() => Dispose();

    public void Dispose()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
        _realtime.Reconnected -= OnReconnected;
        _realtime.StatusChanged -= OnStatusChanged;
    }

    private void OnReconnected(object? sender, EventArgs e) => _ = RefreshAsync();

    private void OnStatusChanged(object? sender, ConnectionStatus status) =>
        System.Windows.Application.Current?.Dispatcher.Invoke(() => ConnectionText = ConnectionLabel(status));

    private static string ConnectionLabel(ConnectionStatus status) => status switch
    {
        ConnectionStatus.Connected => "Live",
        ConnectionStatus.Reconnecting or ConnectionStatus.Connecting => "Reconnecting…",
        _ => "Offline: showing the last numbers",
    };

    partial void OnSelectedFilterChanged(string value) => ApplyFilter();

    partial void OnSelectedMovementKindChanged(string value) => _ = LoadMovementsAsync();

    partial void OnShowMovementsChanged(bool value)
    {
        if (value)
        {
            _ = LoadMovementsAsync();
        }
    }

    [RelayCommand]
    private void ShowStockList() => ShowMovements = false;

    [RelayCommand]
    private void ShowMovementList() => ShowMovements = CanSeeMovements;

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (Interlocked.Exchange(ref _refreshing, 1) == 1)
        {
            return;
        }

        try
        {
            var result = await _menuApi.GetStockOverviewAsync();
            if (!result.Success || result.Data is null)
            {
                ErrorMessage = ApiFailures.Describe(result);
                return;
            }

            ErrorMessage = null;
            _all = result.Data.Items.Select(i => new StockRowViewModel(i)).ToList();
            LastSyncedText = "Last synced " + result.Data.ServerTimeUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);
            ApplyFilter();
            if (ShowMovements)
            {
                await LoadMovementsAsync();
            }
        }
        finally
        {
            Interlocked.Exchange(ref _refreshing, 0);
        }
    }

    private void ApplyFilter()
    {
        var visible = SelectedFilter switch
        {
            LowFilter => _all.Where(r => r.IsLow),
            OutFilter => _all.Where(r => r.IsOut),
            _ => _all,
        };
        Rows.Clear();
        foreach (var row in visible)
        {
            Rows.Add(row);
        }

        var low = _all.Count(r => r.IsLow);
        var outOf = _all.Count(r => r.IsOut);
        SummaryText = _all.Count == 0
            ? "No item has a stock count yet. Set one on the Kitchen Display (Item availability)."
            : $"{_all.Count} counted items · {low} low · {outOf} out or unavailable";
        OnPropertyChanged(nameof(IsEmpty));
    }

    private async Task LoadMovementsAsync()
    {
        if (!CanSeeMovements)
        {
            return;
        }

        var type = MovementKinds.FirstOrDefault(k => k.Label == SelectedMovementKind).Type;
        var result = await _menuApi.GetStockMovementsAsync(type, null);
        if (!result.Success || result.Data is null)
        {
            ErrorMessage = ApiFailures.Describe(result);
            return;
        }

        Movements.Clear();
        foreach (var movement in result.Data)
        {
            Movements.Add(new StockHistoryRow(movement));
        }
    }
}
