using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Auth;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Orders;
using HotelPOS.Desktop.Services.Realtime;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Modules.Tables;

public sealed partial class TableMapSectionViewModel : ObservableObject
{
    public TableMapSectionViewModel(int id, string name)
    {
        Id = id;
        Name = name;
    }

    public int Id { get; }

    public string Name { get; }

    public ObservableCollection<TableTileViewModel> Tables { get; } = new();

    [ObservableProperty]
    private bool _isVisible = true;
}

public sealed record SectionFilter(int? SectionId, string Name);

public sealed partial class TableMapViewModel : ObservableObject, INavigationAware, IRefreshable, IDisposable
{
    private readonly IFloorApi _floorApi;
    private readonly IRealtimeClient _realtime;
    private readonly Dictionary<int, TableTileViewModel> _tiles = new();
    private readonly List<IDisposable> _subscriptions = new();
    private int? _selectAfterLoad;
    private CancellationTokenSource? _timer;
    private TimeSpan _serverClockOffset;
    private int _refreshing;

    public TableMapViewModel(
        IFloorApi floorApi,
        IOrdersApi ordersApi,
        ILocalDraftStore drafts,
        IRealtimeClient realtime,
        INavigationService navigation,
        IAuthSession session,
        IDialogService dialogs,
        INotificationService notifications)
    {
        _floorApi = floorApi;
        _realtime = realtime;
        var roles = session.User?.Roles ?? Array.Empty<string>();
        var isManager = roles.Contains(Roles.Manager) || roles.Contains(Roles.Admin);
        var canTakeOrders = roles.Contains(Roles.Waiter) || isManager;
        Details = new TableDetailsViewModel(floorApi, ordersApi, drafts, navigation, dialogs, notifications, ApplyTable, RefreshAsync, canTakeOrders, isManager,
            roles.Contains(Roles.Cashier));
    }

    public ObservableCollection<TableMapSectionViewModel> Sections { get; } = new();

    public ObservableCollection<SectionFilter> Filters { get; } = new();

    public TableDetailsViewModel Details { get; }

    [ObservableProperty]
    private SectionFilter? _selectedFilter;

    [ObservableProperty]
    private TableTileViewModel? _selectedTable;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    public bool IsEmpty => _tiles.Count == 0 && !IsBusy && ErrorMessage is null;

    public DateTime ServerNowUtc => DateTime.UtcNow + _serverClockOffset;

    public IReadOnlyCollection<TableTileViewModel> AllTables => _tiles.Values;

    public async Task OnNavigatedToAsync(object? parameter)
    {
        _selectAfterLoad = parameter as int?;
        _subscriptions.Add(_realtime.Subscribe<TableStatusChangedEvent>(HubEvents.TableStatusChanged, OnTableStatusChanged));
        _subscriptions.Add(_realtime.Subscribe<OrderUpdatedEvent>(HubEvents.OrderUpdated, e => OnOrderChanged(e.OrderId)));
        _subscriptions.Add(_realtime.Subscribe<OrderCancelledEvent>(HubEvents.OrderCancelled, e => OnOrderChanged(e.OrderId)));
        _subscriptions.Add(_realtime.Subscribe<BillUpdatedEvent>(HubEvents.BillUpdated, e => OnOrderChanged(e.OrderId)));
        _subscriptions.Add(_realtime.Subscribe<KitchenTicketUpdatedEvent>(HubEvents.KitchenTicketUpdated, e => OnOrderChanged(e.OrderId)));
        foreach (var progress in new[] { HubEvents.OrderAccepted, HubEvents.OrderPreparing, HubEvents.OrderReady, HubEvents.OrderServed })
        {
            _subscriptions.Add(_realtime.Subscribe<OrderProgressEvent>(progress, e => OnOrderChanged(e.OrderId)));
        }
        _timer = new CancellationTokenSource();
        _ = RunElapsedTimerAsync(_timer.Token);
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
        _timer?.Cancel();
        _timer?.Dispose();
        _timer = null;
    }

    public async Task RefreshAsync()
    {
        if (Interlocked.Exchange(ref _refreshing, 1) == 1)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _floorApi.GetMapAsync();
            if (!result.Success || result.Data is null)
            {
                ErrorMessage = result.IsConnectionFailure
                    ? "Connection unavailable. The map will refresh when the server is back."
                    : result.Message;
                return;
            }

            ApplyMap(result.Data);
        }
        finally
        {
            IsBusy = false;
            Interlocked.Exchange(ref _refreshing, 0);
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    public void ApplyMap(TableMapDto map)
    {
        _serverClockOffset = map.ServerTimeUtc - DateTime.UtcNow;
        var selectedId = SelectedTable?.Id;

        var seen = new HashSet<int>();
        Sections.Clear();
        foreach (var section in map.Sections)
        {
            var sectionVm = new TableMapSectionViewModel(section.Id, section.Name);
            foreach (var table in section.Tables)
            {
                seen.Add(table.Id);
                if (_tiles.TryGetValue(table.Id, out var tile))
                {
                    if (tile.StateTimestampUtc <= map.ServerTimeUtc)
                    {
                        tile.Apply(table, map.ServerTimeUtc);
                    }
                }
                else
                {
                    tile = new TableTileViewModel(table, map.ServerTimeUtc);
                    _tiles[table.Id] = tile;
                }

                tile.UpdateElapsed(ServerNowUtc);
                sectionVm.Tables.Add(tile);
            }

            Sections.Add(sectionVm);
        }

        foreach (var removed in _tiles.Keys.Where(id => !seen.Contains(id)).ToList())
        {
            _tiles.Remove(removed);
        }

        RebuildFilters();
        var select = _selectAfterLoad ?? selectedId;
        _selectAfterLoad = null;
        SelectedTable = select is { } id && _tiles.TryGetValue(id, out var keep) ? keep : null;
        UpdateSummary();
    }

    public void ApplyTable(TableDto table)
    {
        if (_tiles.TryGetValue(table.Id, out var tile))
        {
            tile.Apply(table, tile.StateTimestampUtc);
            tile.UpdateElapsed(ServerNowUtc);
            UpdateSummary();
            Details.OnTableChanged();
        }
    }

    partial void OnSelectedFilterChanged(SectionFilter? value)
    {
        foreach (var section in Sections)
        {
            section.IsVisible = value?.SectionId is null || value.SectionId == section.Id;
        }
    }

    partial void OnSelectedTableChanged(TableTileViewModel? oldValue, TableTileViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsSelected = false;
        }

        if (newValue is not null)
        {
            newValue.IsSelected = true;
        }

        Details.Show(newValue);
    }

    [RelayCommand]
    private void SelectTable(TableTileViewModel? table) => SelectedTable = table;

    [RelayCommand]
    private Task RefreshMapAsync() => RefreshAsync();

    private void OnTableStatusChanged(TableStatusChangedEvent change)
    {
        if (!_tiles.TryGetValue(change.TableId, out var tile))
        {
            // A table this map does not know (just created or reactivated): reload everything.
            _ = RefreshAsync();
            return;
        }

        if (tile.Apply(change))
        {
            tile.UpdateElapsed(ServerNowUtc);
            UpdateSummary();
            Details.OnTableChanged();
        }
    }

    private void OnOrderChanged(int orderId)
    {
        if (Details.Order?.Id == orderId)
        {
            _ = Details.ReloadOrderAsync();
        }
    }

    private void RebuildFilters()
    {
        var selectedId = SelectedFilter?.SectionId;
        Filters.Clear();
        Filters.Add(new SectionFilter(null, "All"));
        foreach (var section in Sections)
        {
            Filters.Add(new SectionFilter(section.Id, section.Name));
        }

        SelectedFilter = Filters.FirstOrDefault(f => f.SectionId == selectedId) ?? Filters[0];
        OnSelectedFilterChanged(SelectedFilter);
    }

    private void UpdateSummary()
    {
        var available = _tiles.Values.Count(t => t.Status == TableStatus.Available);
        var inUse = _tiles.Values.Count(t => t.Status is not (TableStatus.Available or TableStatus.OutOfService));
        SummaryText = $"{available} available · {inUse} in use · {_tiles.Count} tables";
    }

    private async Task RunElapsedTimerAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                var now = ServerNowUtc;
                foreach (var tile in _tiles.Values)
                {
                    tile.UpdateElapsed(now);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Page closed.
        }
    }
}
