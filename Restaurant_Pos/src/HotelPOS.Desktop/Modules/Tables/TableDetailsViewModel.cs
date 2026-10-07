using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Kitchen;
using HotelPOS.Contracts.Orders;
using HotelPOS.Desktop.Modules.Common;
using HotelPOS.Desktop.Modules.Orders;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Orders;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Modules.Tables;

public sealed record OrderBatchViewModel(string Title, IReadOnlyList<OrderLineViewModel> Lines);

public sealed record OrderLineViewModel(int ItemId, string Text, string Detail, string Total, bool IsCancelled, bool CanCancel)
{
    public bool HasDetail => Detail.Length > 0;
}

public sealed partial class TableDetailsViewModel : ObservableObject
{
    public const string ChangedByAnotherTerminal = "Table was changed by another terminal. The latest status is shown.";

    private const int MaxGuestDigits = 2;

    private readonly IFloorApi _floorApi;
    private readonly IOrdersApi _ordersApi;
    private readonly ILocalDraftStore _drafts;
    private readonly INavigationService _navigation;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;
    private readonly Action<TableDto> _applyTable;
    private readonly Func<Task> _refreshMap;
    private readonly bool _canTakeOrders;
    private readonly bool _isManager;

    public TableDetailsViewModel(
        IFloorApi floorApi,
        IOrdersApi ordersApi,
        ILocalDraftStore drafts,
        INavigationService navigation,
        IDialogService dialogs,
        INotificationService notifications,
        Action<TableDto> applyTable,
        Func<Task> refreshMap,
        bool canTakeOrders,
        bool isManager = false)
    {
        _floorApi = floorApi;
        _ordersApi = ordersApi;
        _drafts = drafts;
        _navigation = navigation;
        _dialogs = dialogs;
        _notifications = notifications;
        _applyTable = applyTable;
        _refreshMap = refreshMap;
        _canTakeOrders = canTakeOrders;
        _isManager = isManager;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpen), nameof(OccupiedSinceText), nameof(ShowKeypad))]
    private TableTileViewModel? _table;

    [ObservableProperty]
    private string _guestInput = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOrder), nameof(OrderTitle), nameof(OrderStatusText), nameof(OrderTotalText), nameof(ShowKeypad))]
    private OrderDetailDto? _order;

    [ObservableProperty]
    private IReadOnlyList<OrderBatchViewModel> _batches = Array.Empty<OrderBatchViewModel>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewOrderText))]
    private bool _hasLocalDraft;

    public bool IsOpen => Table is not null;

    public bool HasOrder => Order is not null;

    public bool ShowKeypad => Table?.IsAvailable == true;

    public string OccupiedSinceText => Table?.OccupiedAtUtc is { } since ? since.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture) : "—";

    public string OrderTitle => Order is null ? "No order" : $"Order #{Order.OrderNumber} · {Order.WaiterName}";

    public string OrderStatusText => Order is null ? string.Empty : StatusLabels.For(Order.Status);

    public string OrderTotalText => Order is null ? string.Empty : $"approx. {Order.ApproxSubtotal.ToString("N2", CultureInfo.CurrentCulture)}";

    public IReadOnlyList<string> Keys { get; } = new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9" };

    public bool ShowOccupy => Table?.IsAvailable == true;

    public bool ShowRelease => Table is { IsOccupied: true, CurrentOrderId: null };

    public bool ShowNewOrder => _canTakeOrders && Table is { CurrentOrderId: null } table && (table.IsAvailable || table.IsOccupied);

    public bool ShowOpenOrder => Order is { Status: OrderStatus.Draft, CanModify: true };

    public bool ShowAddItems => Order is { CanModify: true } order
        && order.Status is OrderStatus.Submitted or OrderStatus.Accepted or OrderStatus.Preparing or OrderStatus.Ready or OrderStatus.Served;

    public bool ShowServe => _canTakeOrders && Order is { } order && order.Tickets.Any(t => t.Status == KitchenOrderStatus.Ready);

    public bool ShowCancelOrder => Order is { CanModify: true } order
        && order.Status is OrderStatus.Draft or OrderStatus.Submitted or OrderStatus.Accepted or OrderStatus.Preparing or OrderStatus.Ready;

    public string NewOrderText => HasLocalDraft ? "RESUME ORDER" : "NEW ORDER";

    public void Show(TableTileViewModel? table)
    {
        Table = table;
        GuestInput = string.Empty;
        ErrorMessage = null;
        Order = null;
        Batches = Array.Empty<OrderBatchViewModel>();
        _ = ReloadOrderAsync();
    }

    public void OnTableChanged()
    {
        OnPropertyChanged(nameof(OccupiedSinceText));
        OnPropertyChanged(nameof(ShowKeypad));
        if (Table?.CurrentOrderId != Order?.Id)
        {
            _ = ReloadOrderAsync();
        }

        RefreshCommands();
    }

    public async Task ReloadOrderAsync()
    {
        var table = Table;
        HasLocalDraft = table is not null && _drafts.Exists(table.Id);
        if (table?.CurrentOrderId is not { } orderId)
        {
            Order = null;
            Batches = Array.Empty<OrderBatchViewModel>();
            RefreshCommands();
            return;
        }

        var result = await _ordersApi.GetAsync(orderId);
        if (!ReferenceEquals(table, Table))
        {
            return;
        }

        if (result.Success && result.Data is not null)
        {
            Order = result.Data;
            var order = result.Data;
            Batches = order.Items
                .GroupBy(i => i.BatchNumber)
                .OrderBy(g => g.Key)
                .Select(g => new OrderBatchViewModel(
                    order.Status == OrderStatus.Draft ? "Not sent yet" : BatchTitle(order, g.Key),
                    g.Select(i => ToLine(i, CanCancelItem(order, i))).ToList()))
                .ToList();
        }
        else
        {
            ErrorMessage = result.Message;
        }

        RefreshCommands();
    }

    [RelayCommand]
    private void Close() => Table = null;

    [RelayCommand]
    private void Digit(string? digit)
    {
        if (digit is { Length: 1 } && char.IsAsciiDigit(digit[0]) && GuestInput.Length < MaxGuestDigits
            && !(GuestInput.Length == 0 && digit == "0"))
        {
            GuestInput += digit;
            ErrorMessage = null;
        }
    }

    [RelayCommand]
    private void Backspace()
    {
        if (GuestInput.Length > 0)
        {
            GuestInput = GuestInput[..^1];
        }
    }

    [RelayCommand]
    private void ClearInput() => GuestInput = string.Empty;

    private bool CanOccupy() => !IsBusy && Table?.IsAvailable == true;

    [RelayCommand(CanExecute = nameof(CanOccupy))]
    private async Task OccupyAsync()
    {
        var table = Table;
        if (table is null || !TryGuests(out var guests))
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _floorApi.OccupyAsync(table.Id, new OccupyTableRequest { GuestCount = guests, RowVersion = table.RowVersion });
            if (result.Success && result.Data is not null)
            {
                _applyTable(result.Data);
                GuestInput = string.Empty;
                _notifications.Success($"Table {table.Code} occupied ({guests} guests).");
                return;
            }

            HandleFailure(result, "occupied");
        }
        finally
        {
            IsBusy = false;
            RefreshCommands();
        }
    }

    private bool CanRelease() => !IsBusy && ShowRelease;

    [RelayCommand(CanExecute = nameof(CanRelease))]
    private async Task ReleaseAsync()
    {
        var table = Table;
        if (table is null || !await _dialogs.ConfirmAsync(
                "Release table",
                $"Release table {table.Code}? It becomes available for new guests.",
                "Release"))
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _floorApi.ReleaseAsync(table.Id);
            if (result.Success && result.Data is not null)
            {
                _applyTable(result.Data);
                _notifications.Success($"Table {table.Code} released.");
                return;
            }

            HandleFailure(result, "released");
        }
        finally
        {
            IsBusy = false;
            RefreshCommands();
        }
    }

    private bool CanNewOrder() => !IsBusy && ShowNewOrder;

    [RelayCommand(CanExecute = nameof(CanNewOrder))]
    private async Task NewOrderAsync()
    {
        var table = Table!;
        int guests;
        if (table.IsAvailable)
        {
            var draft = _drafts.Load(table.Id);
            if (GuestInput.Length == 0 && draft is { Mode: OrderBuilderMode.NewOrder })
            {
                guests = draft.GuestCount;
            }
            else if (!TryGuests(out guests))
            {
                return;
            }
        }
        else
        {
            guests = table.GuestCount ?? 1;
        }

        await _navigation.NavigateToPageAsync<OrderBuilderViewModel>(
            new OrderBuilderContext(table.Id, table.Code, OrderBuilderMode.NewOrder, guests));
    }

    private bool CanOpenOrder() => !IsBusy && ShowOpenOrder;

    [RelayCommand(CanExecute = nameof(CanOpenOrder))]
    private Task OpenOrderAsync() =>
        _navigation.NavigateToPageAsync<OrderBuilderViewModel>(
            new OrderBuilderContext(Table!.Id, Table.Code, OrderBuilderMode.EditDraft, Order!.GuestCount, Order.Id, Order.OrderNumber));

    private bool CanAddItems() => !IsBusy && ShowAddItems;

    [RelayCommand(CanExecute = nameof(CanAddItems))]
    private Task AddItemsAsync() =>
        _navigation.NavigateToPageAsync<OrderBuilderViewModel>(
            new OrderBuilderContext(Table!.Id, Table.Code, OrderBuilderMode.AppendItems, Order!.GuestCount, Order.Id, Order.OrderNumber));

    private bool CanCancelOrder() => !IsBusy && ShowCancelOrder;

    [RelayCommand(CanExecute = nameof(CanCancelOrder))]
    private async Task CancelOrderAsync()
    {
        var order = Order!;
        var reason = await _dialogs.PromptAsync(
            $"Cancel order #{order.OrderNumber}",
            "Why is the order cancelled? (Required once the kitchen has started.)",
            "Cancel order");
        if (reason is null)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _ordersApi.CancelAsync(order.Id, new CancelOrderRequest { Reason = reason });
            if (result.Success)
            {
                _drafts.Delete(order.TableId);
                _notifications.Success($"Order #{order.OrderNumber} cancelled.");
                await _refreshMap();
            }
            else
            {
                ErrorMessage = ApiFailures.Describe(result);
            }
        }
        finally
        {
            IsBusy = false;
            await ReloadOrderAsync();
        }
    }

    private bool CanServe() => !IsBusy && ShowServe;

    [RelayCommand(CanExecute = nameof(CanServe))]
    private async Task ServeAsync()
    {
        var order = Order!;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _ordersApi.ServeAsync(order.Id);
            if (result.Success)
            {
                _notifications.Success($"Order #{order.OrderNumber} served.");
            }
            else
            {
                ErrorMessage = ApiFailures.Describe(result);
            }
        }
        finally
        {
            IsBusy = false;
            await ReloadOrderAsync();
        }
    }

    [RelayCommand]
    private async Task CancelItemAsync(OrderLineViewModel? line)
    {
        var order = Order;
        if (line is null || order is null || !line.CanCancel)
        {
            return;
        }

        var reason = await _dialogs.PromptAsync("Cancel item", $"Cancel {line.Text}? Enter the reason for the kitchen.", "Cancel item");
        if (reason is null)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _ordersApi.CancelItemAsync(order.Id, line.ItemId, new CancelOrderItemRequest { Reason = reason });
            if (result.Success)
            {
                _notifications.Success($"{line.Text} cancelled.");
            }
            else
            {
                ErrorMessage = ApiFailures.Describe(result);
            }
        }
        finally
        {
            IsBusy = false;
            await ReloadOrderAsync();
        }
    }

    partial void OnIsBusyChanged(bool value) => RefreshCommands();

    private bool CanCancelItem(OrderDetailDto order, OrderItemDto item) => order.CanModify && item.Status == OrderItemStatus.Sent
        && (item.TicketStatus is KitchenOrderStatus.New or KitchenOrderStatus.Accepted
            || (item.TicketStatus == KitchenOrderStatus.Preparing && _isManager));

    private static string BatchTitle(OrderDetailDto order, int batch)
    {
        var tickets = order.Tickets.Where(t => t.BatchNumber == batch).ToList();
        var states = tickets.Count == 1
            ? tickets[0].Status.ToString()
            : string.Join(" · ", tickets.Select(t => $"{t.StationCode} {t.Status}"));
        return tickets.Count == 0 ? $"Batch {batch}" : $"Batch {batch} · {states}";
    }

    private bool TryGuests(out int guests)
    {
        if (int.TryParse(GuestInput, out guests) && guests is >= FloorLimits.MinGuests and <= FloorLimits.MaxGuests)
        {
            return true;
        }

        ErrorMessage = "Enter the number of guests on the keypad.";
        return false;
    }

    private void RefreshCommands()
    {
        ServeCommand.NotifyCanExecuteChanged();
        foreach (var name in new[] { nameof(ShowServe), nameof(ShowOccupy), nameof(ShowRelease), nameof(ShowNewOrder), nameof(ShowOpenOrder), nameof(ShowAddItems), nameof(ShowCancelOrder), nameof(NewOrderText) })
        {
            OnPropertyChanged(name);
        }

        OccupyCommand.NotifyCanExecuteChanged();
        ReleaseCommand.NotifyCanExecuteChanged();
        NewOrderCommand.NotifyCanExecuteChanged();
        OpenOrderCommand.NotifyCanExecuteChanged();
        AddItemsCommand.NotifyCanExecuteChanged();
        CancelOrderCommand.NotifyCanExecuteChanged();
    }

    private static OrderLineViewModel ToLine(OrderItemDto item, bool canCancel) => new(
        item.Id,
        $"{item.Quantity} × {item.ItemName}",
        string.Join(" · ", item.Modifiers.Select(m => m.Name).Concat(item.Notes is null ? Array.Empty<string>() : new[] { $"“{item.Notes}”" })),
        item.LineTotal.ToString("N2", CultureInfo.CurrentCulture),
        item.Status == OrderItemStatus.Cancelled,
        canCancel);

    private void HandleFailure(ApiResult<TableDto> result, string action)
    {
        if (result.IsConnectionFailure)
        {
            ErrorMessage = $"Connection unavailable. The table may not have been {action}; its status will refresh when the connection is back.";
            return;
        }

        if (result.HasError(ErrorCodes.ConcurrencyConflict) || result.HasError(ErrorCodes.TableNotAvailable)
            || result.HasError(ErrorCodes.InvalidStateTransition))
        {
            if (result.Data is not null)
            {
                _applyTable(result.Data);
            }
            else
            {
                _ = _refreshMap();
            }

            ErrorMessage = ChangedByAnotherTerminal;
            return;
        }

        ErrorMessage = result.Message;
    }
}
