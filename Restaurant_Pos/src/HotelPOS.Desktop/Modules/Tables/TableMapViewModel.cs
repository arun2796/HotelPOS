using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Navigation;
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

/// <summary>A section filter chip. <see cref="SectionId"/> is null for "All".</summary>
public sealed record SectionFilter(int? SectionId, string Name);

/// <summary>
/// Waiter/Cashier/Manager table map. Loads from the API, then stays current through
/// <c>TableStatusChanged</c> events; the shell calls <see cref="RefreshAsync"/> after a reconnect, and the
/// API state always wins (docs/04 § 5).
/// </summary>
public sealed partial class TableMapViewModel : ObservableObject, INavigationAware, IRefreshable, IDisposable
{
    private readonly IFloorApi _floorApi;
    private readonly IRealtimeClient _realtime;
    private readonly Dictionary<int, TableTileViewModel> _tiles = new();
    private IDisposable? _subscription;
    private CancellationTokenSource? _timer;
    private TimeSpan _serverClockOffset;
    private int _refreshing;

    public TableMapViewModel(IFloorApi floorApi, IRealtimeClient realtime, IDialogService dialogs, INotificationService notifications)
    {
        _floorApi = floorApi;
        _realtime = realtime;
        Details = new TableDetailsViewModel(floorApi, dialogs, notifications, ApplyTable, RefreshAsync);
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

    /// <summary>The server's current time, estimated from the clock offset measured at the last refresh.</summary>
    public DateTime ServerNowUtc => DateTime.UtcNow + _serverClockOffset;

    public IReadOnlyCollection<TableTileViewModel> AllTables => _tiles.Values;

    public async Task OnNavigatedToAsync(object? parameter)
    {
        _subscription = _realtime.Subscribe<TableStatusChangedEvent>(HubEvents.TableStatusChanged, OnTableStatusChanged);
        _timer = new CancellationTokenSource();
        _ = RunElapsedTimerAsync(_timer.Token);
        await RefreshAsync();
    }

    public void OnNavigatedFrom() => Dispose();

    public void Dispose()
    {
        _subscription?.Dispose();
        _subscription = null;
        _timer?.Cancel();
        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>Reloads the whole map from the API. Concurrent calls collapse into the running one.</summary>
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

    /// <summary>Replaces the map with the server's. A tile that already shows a newer event keeps it.</summary>
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
        SelectedTable = selectedId is { } id && _tiles.TryGetValue(id, out var keep) ? keep : null;
        UpdateSummary();
    }

    /// <summary>Applies a single table returned by the API (occupy/release result or conflict data).</summary>
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
