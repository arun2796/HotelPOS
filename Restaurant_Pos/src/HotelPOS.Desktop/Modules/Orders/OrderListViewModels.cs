using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Orders;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using HotelPOS.Desktop.Modules.Common;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Auth;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Realtime;

namespace HotelPOS.Desktop.Modules.Orders;

public sealed record OrderRow(OrderSummaryDto Order, string ElapsedText, string TotalText)
{
    public string Title => $"#{Order.OrderNumber} · {Order.TableCode}";

    public string StatusText => StatusLabels.For(Order.Status);

    public bool IsDraft => Order.Status == OrderStatus.Draft;

    public bool IsReady => Order.Status == OrderStatus.Ready;

    public string Detail => $"{Order.ItemCount} items · {Order.GuestCount} guests · {Order.WaiterName}";
}

public abstract partial class OrderListViewModelBase : ObservableObject, INavigationAware, IRefreshable, IDisposable
{
    private readonly IOrdersApi _ordersApi;
    private readonly IRealtimeClient _realtime;
    private readonly INavigationService _navigation;
    private readonly List<IDisposable> _subscriptions = new();
    private int _refreshing;

    protected OrderListViewModelBase(IOrdersApi ordersApi, IRealtimeClient realtime, INavigationService navigation, IAuthSession session)
    {
        _ordersApi = ordersApi;
        _realtime = realtime;
        _navigation = navigation;
        Session = session;
        var roles = session.User?.Roles ?? Array.Empty<string>();
        CanOpenTables = roles.Any(r => r is Roles.Waiter or Roles.Cashier or Roles.Manager or Roles.Admin);
    }

    public ObservableCollection<OrderRow> Rows { get; } = new();

    public bool CanOpenTables { get; }

    public abstract string EmptyText { get; }

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    protected IAuthSession Session { get; }

    public async Task OnNavigatedToAsync(object? parameter)
    {
        foreach (var eventName in new[] { HubEvents.OrderCreated, HubEvents.OrderUpdated, HubEvents.OrderCancelled, HubEvents.TableStatusChanged })
        {
            _subscriptions.Add(_realtime.Subscribe<RealtimeNotice>(eventName, notice => _ = RefreshAsync()));
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
            var now = DateTime.UtcNow;
            Rows.Clear();
            foreach (var order in result.Data.Where(Include))
            {
                var since = order.SubmittedAtUtc ?? order.CreatedAtUtc;
                var minutes = Math.Max(0, (int)(now - since).TotalMinutes);
                Rows.Add(new OrderRow(order, minutes < 60 ? $"{minutes} min" : $"{minutes / 60} h {minutes % 60:00} min",
                    order.ApproxTotal.ToString("N2", CultureInfo.CurrentCulture)));
            }

            IsEmpty = Rows.Count == 0;
            SummaryText = $"{Rows.Count} active order{(Rows.Count == 1 ? string.Empty : "s")}";
        }
        finally
        {
            IsBusy = false;
            Interlocked.Exchange(ref _refreshing, 0);
        }
    }

    protected abstract bool Include(OrderSummaryDto order);

    [RelayCommand]
    private Task RefreshListAsync() => RefreshAsync();

    [RelayCommand]
    private Task OpenAsync(OrderRow? row) =>
        row is not null && CanOpenTables ? _navigation.NavigateToAsync(ModuleRegistry.Tables, row.Order.TableId) : Task.CompletedTask;

    // Any event payload: the list is simply reloaded.
    private sealed record RealtimeNotice;
}

public sealed class MyOrdersViewModel : OrderListViewModelBase
{
    public MyOrdersViewModel(IOrdersApi ordersApi, IRealtimeClient realtime, INavigationService navigation, IAuthSession session)
        : base(ordersApi, realtime, navigation, session)
    {
    }

    public override string EmptyText => "You have no open orders. Open a table to start one.";

    protected override bool Include(OrderSummaryDto order) => order.WaiterId == Session.User?.Id;
}

public sealed class ActiveOrdersViewModel : OrderListViewModelBase
{
    public ActiveOrdersViewModel(IOrdersApi ordersApi, IRealtimeClient realtime, INavigationService navigation, IAuthSession session)
        : base(ordersApi, realtime, navigation, session)
    {
    }

    public override string EmptyText => "No active orders.";

    protected override bool Include(OrderSummaryDto order) => order.Status != OrderStatus.Draft;
}
