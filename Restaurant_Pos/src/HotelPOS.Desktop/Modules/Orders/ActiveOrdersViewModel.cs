using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Orders;
using HotelPOS.Contracts.Presentation;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using HotelPOS.Desktop.Modules.Billing;
using HotelPOS.Desktop.Modules.Common;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Auth;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Orders;
using HotelPOS.Desktop.Services.Realtime;

namespace HotelPOS.Desktop.Modules.Orders;

/// <summary>One chip above the Orders list: a status (or all) with how many active orders carry it.</summary>
public sealed record OrderStatusFilter(OrderStatus? Status, string Label, int Count)
{
    public string Text => $"{Label} ({Count})";
}

public sealed record OrderChannelFilter(OrderType? Channel, string Label, int Count)
{
    public string Text => Count > 0 ? $"{Label} ({Count})" : Label;
}

/// <summary>One line of the detail pane: an order item with its modifiers and note, as the kitchen saw it.</summary>
public sealed record OrderDetailLine(OrderItemDto Item)
{
    public int Quantity => Item.Quantity;

    public string Name => Item.ItemName;

    public string Modifiers => string.Join(", ", Item.Modifiers.Select(m => m.Name));

    public string Note => Item.Notes ?? string.Empty;

    public string PriceText => Money.Format(Item.LineTotal);

    public bool IsCancelled => Item.Status == OrderItemStatus.Cancelled;

    public StatusDisplay Display => StatusPresentation.For(Item.Status);
}

/// <summary>
/// Phase 21 A1: every active order in the restaurant (managers), over the existing <c>GET api/orders/active</c>: search,
/// status chips, sort and a detail pane with the items from <c>GET api/orders/{id}</c>. "View" opens the table, as My Orders does.
/// </summary>
public sealed partial class ActiveOrdersViewModel : ViewModelBase, INavigationAware, IRefreshable, IDisposable
{
    private readonly IOrdersApi _ordersApi;
    private readonly IRealtimeClient _realtime;
    private readonly INavigationService _navigation;
    private readonly List<IDisposable> _subscriptions = new();
    private List<OrderSummaryDto> _all = new();
    private int _refreshing;

    public ActiveOrdersViewModel(IOrdersApi ordersApi, IRealtimeClient realtime, INavigationService navigation, IAuthSession session)
    {
        _ordersApi = ordersApi;
        _realtime = realtime;
        _navigation = navigation;
        var roles = session.User?.Roles ?? Array.Empty<string>();
        CanOpenTables = roles.Any(r => r is Roles.Waiter or Roles.Cashier or Roles.Manager or Roles.Admin);
        Filters.Add(new OrderStatusFilter(null, "All", 0));
        _selectedFilter = Filters[0];
        ChannelFilters.Add(new OrderChannelFilter(null, "All", 0));
        _selectedChannelFilter = ChannelFilters[0];
    }

    public bool CanOpenTables { get; }

    public ObservableCollection<OrderRow> Rows { get; } = new();

    public ObservableCollection<OrderStatusFilter> Filters { get; } = new();

    public ObservableCollection<OrderChannelFilter> ChannelFilters { get; } = new();

    public IReadOnlyList<SortOption> SortOptions => ActiveOrderQueries.SortOptions;

    public ObservableCollection<OrderDetailLine> DetailLines { get; } = new();

    [ObservableProperty]
    private OrderStatusFilter? _selectedFilter;

    [ObservableProperty]
    private OrderChannelFilter? _selectedChannelFilter;

    [ObservableProperty]
    private string _search = string.Empty;

    [ObservableProperty]
    private SortOption _selectedSort = ActiveOrderQueries.SortOptions[0];

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(OpenButtonText), nameof(CanOpenSelection))]
    private OrderRow? _selectedRow;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetail), nameof(DetailTitle), nameof(DetailSubtitle), nameof(DetailTotalText), nameof(DetailNotes), nameof(HasDetailNotes))]
    private OrderDetailDto? _detail;

    [ObservableProperty]
    private string? _detailError;

    public bool HasSelection => SelectedRow is not null;

    public bool HasDetail => Detail is not null;

    public string OpenButtonText => SelectedRow?.IsDraft == true ? "Resume draft" : "Open table";

    public bool CanOpenSelection => SelectedRow?.IsDraft == true || CanOpenTables;

    public string DetailTitle => Detail is { } d
        ? d.OrderType switch
        {
            OrderType.Takeaway => $"#{d.OrderNumber} · Takeaway",
            OrderType.Delivery => $"#{d.OrderNumber} · Delivery",
            _ => $"#{d.OrderNumber} · {d.TableCode}",
        }
        : SelectedRow?.Title ?? string.Empty;

    public string DetailSubtitle => Detail is { } d
        ? $"{d.WaiterName} · {Plural.Count(d.GuestCount, "guest")} · {StatusLabels.For(d.Status)}"
        : SelectedRow?.Detail ?? string.Empty;

    public string DetailTotalText => Detail is { } d ? Money.Format(d.ApproxSubtotal) : string.Empty;

    public string DetailNotes => Detail?.Notes ?? string.Empty;

    public bool HasDetailNotes => !string.IsNullOrWhiteSpace(Detail?.Notes);

    public async Task OnNavigatedToAsync(object? parameter)
    {
        foreach (var eventName in new[] { HubEvents.OrderCreated, HubEvents.OrderUpdated, HubEvents.OrderCancelled, HubEvents.TableStatusChanged, HubEvents.OrderReady, HubEvents.OrderServed, HubEvents.BillRequested })
        {
            _subscriptions.Add(_realtime.Subscribe<object>(eventName, _ => _ = RefreshAsync()));
        }

        if (parameter is OrderStatus status)
        {
            SelectedFilter = Filters.FirstOrDefault(f => f.Status == status) ?? new OrderStatusFilter(status, StatusLabels.For(status), 0);
        }

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
    }

    public async Task RefreshAsync()
    {
        if (Interlocked.Exchange(ref _refreshing, 1) == 1)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _ordersApi.GetActiveAsync();
            if (!result.Success || result.Data is null)
            {
                ErrorMessage = result.IsConnectionFailure
                    ? "Connection unavailable. The list will refresh when the server is back."
                    : result.Message;
                return;
            }

            ErrorMessage = null;
            _all = result.Data;
            RebuildFilters();
            ApplyFilters();
        }
        finally
        {
            IsBusy = false;
            Interlocked.Exchange(ref _refreshing, 0);
        }
    }

    // Rebuilding the rows re-selects the same order; that must not reload its detail or re-run the filters.
    private bool _rebuilding;

    partial void OnSelectedFilterChanged(OrderStatusFilter? value)
    {
        if (!_rebuilding)
        {
            ApplyFilters();
        }
    }

    partial void OnSelectedChannelFilterChanged(OrderChannelFilter? value)
    {
        if (!_rebuilding)
        {
            ApplyFilters();
        }
    }

    partial void OnSearchChanged(string value) => ApplyFilters();

    partial void OnSelectedSortChanged(SortOption value) => ApplyFilters();

    partial void OnSelectedRowChanged(OrderRow? value)
    {
        if (!_rebuilding)
        {
            _ = LoadDetailAsync(value);
        }
    }

    private void RebuildFilters()
    {
        var current = SelectedFilter?.Status;
        var currentChannel = SelectedChannelFilter?.Channel;

        Filters.Clear();
        Filters.Add(new OrderStatusFilter(null, "All", _all.Count));
        foreach (var (status, count) in ActiveOrderQueries.CountByStatus(_all))
        {
            Filters.Add(new OrderStatusFilter(status, StatusLabels.For(status), count));
        }

        ChannelFilters.Clear();
        ChannelFilters.Add(new OrderChannelFilter(null, "All", _all.Count));
        ChannelFilters.Add(new OrderChannelFilter(OrderType.DineIn, "Dine-In", _all.Count(o => o.OrderType == OrderType.DineIn)));
        ChannelFilters.Add(new OrderChannelFilter(OrderType.Takeaway, "Takeaway", _all.Count(o => o.OrderType == OrderType.Takeaway)));
        ChannelFilters.Add(new OrderChannelFilter(OrderType.Delivery, "Delivery", _all.Count(o => o.OrderType == OrderType.Delivery)));

        // Keep the chip the manager had chosen; a status that has no order left falls back to All.
        _rebuilding = true;
        try
        {
            SelectedFilter = Filters.FirstOrDefault(f => f.Status == current) ?? Filters[0];
            SelectedChannelFilter = ChannelFilters.FirstOrDefault(f => f.Channel == currentChannel) ?? ChannelFilters[0];
        }
        finally
        {
            _rebuilding = false;
        }
    }

    private void ApplyFilters()
    {
        var now = DateTime.UtcNow;
        var visible = ActiveOrderQueries.Apply(_all, SelectedFilter?.Status, Search, SelectedSort.Sort, SelectedChannelFilter?.Channel);
        var selectedId = SelectedRow?.Order.Id;
        Rows.Clear();
        foreach (var order in visible)
        {
            Rows.Add(new OrderRow(order, ActiveOrderQueries.Elapsed(ActiveOrderQueries.Since(order), now), Money.Format(order.ApproxTotal)));
        }

        if (selectedId is { } id)
        {
            _rebuilding = true;
            try
            {
                SelectedRow = Rows.FirstOrDefault(r => r.Order.Id == id);
            }
            finally
            {
                _rebuilding = false;
            }

            if (SelectedRow is null)
            {
                _ = LoadDetailAsync(null);
            }
        }

        IsEmpty = Rows.Count == 0;
        SummaryText = visible.Count == _all.Count
            ? Plural.Count(_all.Count, "active order")
            : $"{visible.Count} of {Plural.Count(_all.Count, "active order")}";
    }

    private async Task LoadDetailAsync(OrderRow? row)
    {
        if (row is null)
        {
            Detail = null;
            DetailLines.Clear();
            DetailError = null;
            return;
        }

        var result = await _ordersApi.GetAsync(row.Order.Id);
        if (SelectedRow?.Order.Id != row.Order.Id)
        {
            return;
        }

        if (!result.Success || result.Data is null)
        {
            Detail = null;
            DetailLines.Clear();
            DetailError = result.IsConnectionFailure ? "Connection unavailable. The items will load when the server is back." : result.Message;
            return;
        }

        DetailError = null;
        Detail = result.Data;
        DetailLines.Clear();
        foreach (var item in result.Data.Items.OrderBy(i => i.BatchNumber).ThenBy(i => i.Id))
        {
            DetailLines.Add(new OrderDetailLine(item));
        }
    }

    [RelayCommand]
    private Task RefreshListAsync() => RefreshAsync();

    [RelayCommand]
    private void CloseDetail() => SelectedRow = null;

    [RelayCommand]
    private void ClearSearch() => Search = string.Empty;

    /// <summary>"View" or "Resume": the table behind the order, or the builder for drafts.</summary>
    [RelayCommand]
    private Task OpenAsync(OrderRow? row)
    {
        if (row is null)
        {
            return Task.CompletedTask;
        }

        if (row.IsDraft)
        {
            var ctx = new OrderBuilderContext(
                row.Order.TableId,
                row.Order.TableCode ?? string.Empty,
                OrderBuilderMode.EditDraft,
                row.Order.GuestCount,
                row.Order.Id,
                row.Order.OrderNumber,
                row.Order.OrderType);
            return _navigation.NavigateToPageAsync<OrderBuilderViewModel>(ctx);
        }

        return row.Order.TableId is { } tableId && CanOpenTables
            ? _navigation.NavigateToAsync(ModuleRegistry.Tables, tableId)
            : Task.CompletedTask;
    }

    [RelayCommand]
    private Task OpenSelectedTableAsync() => OpenAsync(SelectedRow);

    public static string FormatCreated(DateTime createdAtUtc) => createdAtUtc.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture);
}
