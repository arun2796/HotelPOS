using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Orders;
using HotelPOS.Desktop.Modules.Common;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Menu;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Orders;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Modules.Orders;

public sealed record OrderBuilderContext(
    int TableId,
    string TableCode,
    OrderBuilderMode Mode,
    int GuestCount,
    int? OrderId = null,
    int? OrderNumber = null);

public sealed class MenuTileViewModel
{
    public MenuTileViewModel(MenuEntryDto item, string priceText)
    {
        Item = item;
        PriceText = priceText;
    }

    public MenuEntryDto Item { get; }

    public string Name => Item.Name;

    public string PriceText { get; }

    public bool IsSoldOut => !Item.IsAvailable;

    public bool HasModifiers => Item.ModifierGroupIds.Count > 0;
}

public sealed partial class OrderBuilderViewModel : ObservableObject, INavigationAware
{
    public static readonly IReadOnlyList<string> QuickNotes = new[]
    {
        "Less spicy", "Extra spicy", "No onion", "No garlic", "Less oil", "Jain", "Parcel",
    };

    private readonly IMenuCache _menuCache;
    private readonly IOrdersApi _ordersApi;
    private readonly ISystemApi _systemApi;
    private readonly ILocalDraftStore _drafts;
    private readonly IOrderSubmitter _submitter;
    private readonly INavigationService _navigation;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;
    private OrderBuilderContext _context = new(0, string.Empty, OrderBuilderMode.NewOrder, 1);
    private string _orderRowVersion = string.Empty;
    private Guid? _pendingKey;
    private string _currency = string.Empty;
    // Nothing is saved until the stored draft has been restored, or opening the page would overwrite it.
    private bool _restoring = true;

    public OrderBuilderViewModel(
        IMenuCache menuCache,
        IOrdersApi ordersApi,
        ISystemApi systemApi,
        ILocalDraftStore drafts,
        IOrderSubmitter submitter,
        INavigationService navigation,
        IDialogService dialogs,
        INotificationService notifications)
    {
        _menuCache = menuCache;
        _ordersApi = ordersApi;
        _systemApi = systemApi;
        _drafts = drafts;
        _submitter = submitter;
        _navigation = navigation;
        _dialogs = dialogs;
        _notifications = notifications;
        Cart.CollectionChanged += (_, _) => OnCartChanged();
    }

    public ObservableCollection<MenuCategoryDto> Categories { get; } = new();

    public ObservableCollection<MenuTileViewModel> Items { get; } = new();

    public ObservableCollection<CartItemViewModel> Cart { get; } = new();

    public IReadOnlyList<string> Notes => QuickNotes;

    [ObservableProperty]
    private MenuCategoryDto? _selectedCategory;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GuestText))]
    private int _guestCount = 1;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand), nameof(SaveDraftCommand))]
    private bool _isSending;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _connectionBanner;

    [ObservableProperty]
    private ModifierPickerViewModel? _picker;

    [ObservableProperty]
    private CartItemViewModel? _noteTarget;

    [ObservableProperty]
    private string _noteText = string.Empty;

    public OrderBuilderMode Mode => _context.Mode;

    public bool IsAppendMode => Mode == OrderBuilderMode.AppendItems;

    public bool CanSaveDraft => !IsAppendMode;

    public bool CanChangeGuests => !IsAppendMode;

    public string Title => Mode switch
    {
        OrderBuilderMode.AppendItems => $"{_context.TableCode} · Adding to order #{_context.OrderNumber}",
        OrderBuilderMode.EditDraft => $"{_context.TableCode} · Draft order #{_context.OrderNumber}",
        _ => $"{_context.TableCode} · New order",
    };

    public string GuestText => GuestCount == 1 ? "1 guest" : $"{GuestCount} guests";

    public int ItemCount => Cart.Sum(c => c.Quantity);

    public decimal ApproxTotal => Cart.Sum(c => c.LineTotal);

    public string SummaryText => ItemCount == 0
        ? "Cart is empty"
        : $"{ItemCount} item{(ItemCount == 1 ? string.Empty : "s")} · approx. {_currency}{ApproxTotal.ToString("N2", CultureInfo.CurrentCulture)}";

    public string SendText => IsSending ? "Sending…" : IsAppendMode ? "SEND ADDED ITEMS" : "SEND TO KITCHEN";

    public bool IsCartEmpty => Cart.Count == 0;

    public async Task OnNavigatedToAsync(object? parameter)
    {
        if (parameter is OrderBuilderContext context)
        {
            _context = context;
        }

        GuestCount = Math.Max(1, _context.GuestCount);
        OnPropertyChanged(string.Empty);

        if (_menuCache.Menu is null)
        {
            await _menuCache.RefreshAsync();
        }

        var settings = await _systemApi.GetPublicSettingsAsync();
        _currency = settings.Data?.FirstOrDefault(s => s.Key == SettingKeys.CurrencySymbol)?.Value ?? string.Empty;

        BuildMenu();
        _menuCache.Changed += OnMenuChanged;
        await RestoreAsync();
    }

    public void OnNavigatedFrom() => _menuCache.Changed -= OnMenuChanged;

    partial void OnSelectedCategoryChanged(MenuCategoryDto? value) => ShowItems();

    partial void OnGuestCountChanged(int value) => SaveLocalDraft();

    partial void OnIsSendingChanged(bool value) => OnPropertyChanged(nameof(SendText));

    [RelayCommand]
    private void AddItem(MenuTileViewModel? tile)
    {
        if (tile is null || tile.IsSoldOut)
        {
            return;
        }

        ErrorMessage = null;
        if (tile.HasModifiers && _menuCache.Menu is { } menu)
        {
            var groups = tile.Item.ModifierGroupIds
                .Select(id => menu.ModifierGroups.FirstOrDefault(g => g.Id == id))
                .Where(g => g is not null)
                .Cast<MenuModifierGroupDto>();
            Picker = new ModifierPickerViewModel(tile.Item, groups);
            return;
        }

        AddLine(new DraftLine { MenuItemId = tile.Item.Id, Name = tile.Item.Name, UnitPrice = tile.Item.Price });
    }

    [RelayCommand]
    private void ToggleOption(PickerOptionViewModel? option)
    {
        if (option is not null && Picker?.GroupOf(option) is { } group)
        {
            group.Toggle(option);
            Picker.ErrorMessage = null;
        }
    }

    [RelayCommand]
    private void PickerQuantity(string? delta)
    {
        if (Picker is not null && int.TryParse(delta, out var step))
        {
            Picker.Quantity = Math.Clamp(Picker.Quantity + step, 1, OrderLimits.MaxQuantity);
        }
    }

    [RelayCommand]
    private void ConfirmPicker()
    {
        if (Picker?.TryBuild() is { } line)
        {
            AddLine(line);
            Picker = null;
        }
    }

    [RelayCommand]
    private void CancelPicker() => Picker = null;

    [RelayCommand]
    private void Increase(CartItemViewModel? line)
    {
        if (line is { CanIncrease: true })
        {
            line.Quantity++;
        }
    }

    [RelayCommand]
    private void Decrease(CartItemViewModel? line)
    {
        if (line is null)
        {
            return;
        }

        if (line.Quantity > 1)
        {
            line.Quantity--;
        }
        else
        {
            Cart.Remove(line);
        }
    }

    [RelayCommand]
    private void Remove(CartItemViewModel? line)
    {
        if (line is not null)
        {
            Cart.Remove(line);
        }
    }

    [RelayCommand]
    private void EditNote(CartItemViewModel? line)
    {
        NoteTarget = line;
        NoteText = line?.Notes ?? string.Empty;
    }

    [RelayCommand]
    private void AddQuickNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note) || NoteText.Contains(note, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        NoteText = string.IsNullOrWhiteSpace(NoteText) ? note : $"{NoteText.TrimEnd()}, {note}";
    }

    [RelayCommand]
    private void ApplyNote()
    {
        if (NoteTarget is not null)
        {
            var text = NoteText.Trim();
            NoteTarget.Notes = text.Length == 0 ? null : text[..Math.Min(text.Length, OrderLimits.NotesMaxLength)];
        }

        NoteTarget = null;
    }

    [RelayCommand]
    private void CancelNote() => NoteTarget = null;

    [RelayCommand]
    private void IncreaseGuests() => GuestCount = Math.Min(GuestCount + 1, 99);

    [RelayCommand]
    private void DecreaseGuests() => GuestCount = Math.Max(GuestCount - 1, 1);

    [RelayCommand]
    private Task BackAsync() => _navigation.NavigateToAsync(ModuleRegistry.Tables, _context.TableId);

    private bool CanSend() => !IsSending;

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SaveDraftAsync()
    {
        if (IsAppendMode)
        {
            return;
        }

        await RunAsync(submit: false);
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        if (Cart.Count == 0)
        {
            ErrorMessage = "Add at least one item.";
            return;
        }

        await RunAsync(submit: true);
    }

    private async Task RunAsync(bool submit)
    {
        IsSending = true;
        ErrorMessage = null;
        ConnectionBanner = null;
        try
        {
            var outcome = Mode switch
            {
                OrderBuilderMode.AppendItems => await AppendAsync(),
                OrderBuilderMode.EditDraft => await SaveServerDraftAsync(submit),
                _ => await CreateAsync(submit),
            };
            await HandleOutcomeAsync(outcome, submit);
        }
        finally
        {
            IsSending = false;
        }
    }

    private Task<SendOutcome<OrderDetailDto>> CreateAsync(bool submit)
    {
        var key = EnsurePendingKey();
        var request = new CreateOrderRequest
        {
            TableId = _context.TableId,
            GuestCount = GuestCount,
            Items = Cart.Select(c => c.ToInput()).ToList(),
            Submit = submit,
        };
        return _submitter.SendAsync(ct => _ordersApi.CreateAsync(request, key, ct));
    }

    private Task<SendOutcome<OrderDetailDto>> AppendAsync()
    {
        var key = EnsurePendingKey();
        var request = new AppendOrderItemsRequest { Items = Cart.Select(c => c.ToInput()).ToList() };
        return _submitter.SendAsync(ct => _ordersApi.AppendItemsAsync(_context.OrderId!.Value, request, key, ct));
    }

    private async Task<SendOutcome<OrderDetailDto>> SaveServerDraftAsync(bool submit)
    {
        var orderId = _context.OrderId!.Value;
        var replaced = await _submitter.SendAsync(ct => _ordersApi.ReplaceItemsAsync(orderId, new ReplaceOrderItemsRequest
        {
            Items = Cart.Select(c => c.ToInput()).ToList(),
            RowVersion = _orderRowVersion,
        }, ct));
        if (replaced.Status != SendStatus.Succeeded || !submit)
        {
            return replaced;
        }

        _orderRowVersion = replaced.Data!.RowVersion;
        var submitted = await _submitter.SendAsync(ct => _ordersApi.SubmitAsync(orderId, ct));

        // A retry after a lost response finds the order already sent: that is the success we were waiting for.
        return submitted.Status == SendStatus.Rejected && submitted.Data is { Status: not OrderStatus.Draft }
            ? submitted with { Status = SendStatus.Succeeded, LastResult = ApiResult<OrderDetailDto>.Ok(submitted.Data) }
            : submitted;
    }

    private async Task HandleOutcomeAsync(SendOutcome<OrderDetailDto> outcome, bool submit)
    {
        switch (outcome.Status)
        {
            case SendStatus.Succeeded:
                _pendingKey = null;
                _drafts.Delete(_context.TableId);
                var order = outcome.Data!;
                _notifications.Success(submit
                    ? IsAppendMode ? $"Added items sent for order #{order.OrderNumber}." : $"Order #{order.OrderNumber} sent to the kitchen."
                    : $"Draft saved as order #{order.OrderNumber}.");
                await _navigation.NavigateToAsync(ModuleRegistry.Tables, _context.TableId);
                return;

            case SendStatus.Rejected:
                _pendingKey = null;
                SaveLocalDraft();
                ErrorMessage = DescribeRejection(outcome.LastResult);
                return;

            default:
                SaveLocalDraft();
                ConnectionBanner = "Connection unavailable — the order was NOT confirmed. Your items are kept on this terminal.";
                if (await _dialogs.ConfirmAsync(
                        "Order not confirmed",
                        "The server did not answer. Check the Wi-Fi/LAN connection, then retry. Retrying can never create a second order.",
                        "Retry",
                        "Keep draft"))
                {
                    await RunAsync(submit);
                }

                return;
        }
    }

    private static string DescribeRejection(ApiResult<OrderDetailDto> result)
    {
        if (result.HasError(ErrorCodes.TableNotAvailable))
        {
            return $"{result.Message} Go back and open the table's current order.";
        }

        if (result.HasError(ErrorCodes.IdempotencyKeyReused))
        {
            return "This order may already have been sent from an earlier attempt. Go back and check the table before sending again.";
        }

        if (result.HasError(ErrorCodes.ConcurrencyConflict))
        {
            return "The order was changed on another terminal. Go back and open it again.";
        }

        return ApiFailures.Describe(result);
    }

    private Guid EnsurePendingKey()
    {
        _pendingKey ??= Guid.NewGuid();
        SaveLocalDraft();
        return _pendingKey.Value;
    }

    private void AddLine(DraftLine line)
    {
        var existing = Cart.FirstOrDefault(c => c.IsSameChoice(line.MenuItemId, line.ModifierOptionIds.ToList()));
        if (existing is not null)
        {
            existing.Quantity = Math.Min(existing.Quantity + line.Quantity, OrderLimits.MaxQuantity);
            return;
        }

        Cart.Add(new CartItemViewModel(line));
    }

    private async Task RestoreAsync()
    {
        _restoring = true;
        try
        {
            Cart.Clear();
            var local = _drafts.Load(_context.TableId);
            if (local is not null && local.Mode == _context.Mode && local.OrderId == _context.OrderId)
            {
                GuestCount = Math.Max(1, local.GuestCount);
                _pendingKey = local.PendingKey;
                foreach (var line in local.Lines)
                {
                    Cart.Add(new CartItemViewModel(line));
                }
            }

            if (Mode == OrderBuilderMode.EditDraft && _context.OrderId is { } orderId)
            {
                var order = await _ordersApi.GetAsync(orderId);
                if (order.Success && order.Data is not null)
                {
                    _orderRowVersion = order.Data.RowVersion;
                    if (local is null || local.Mode != Mode)
                    {
                        GuestCount = order.Data.GuestCount;
                        foreach (var item in order.Data.Items.Where(i => i.Status != OrderItemStatus.Cancelled))
                        {
                            Cart.Add(new CartItemViewModel(new DraftLine
                            {
                                MenuItemId = item.MenuItemId,
                                Name = item.ItemName,
                                UnitPrice = item.UnitPrice,
                                Quantity = item.Quantity,
                                Notes = item.Notes,
                                ModifierOptionIds = item.Modifiers.Select(m => m.ModifierOptionId).ToList(),
                                ModifierNames = item.Modifiers.Select(m => m.Name).ToList(),
                                ModifierTotal = item.Modifiers.Sum(m => m.PriceDelta),
                            }));
                        }
                    }
                }
                else
                {
                    ErrorMessage = order.Message;
                }
            }
        }
        finally
        {
            _restoring = false;
        }

        OnCartChanged();
    }

    private void OnCartChanged()
    {
        foreach (var line in Cart)
        {
            line.PropertyChanged -= OnLineChanged;
            line.PropertyChanged += OnLineChanged;
        }

        OnPropertyChanged(nameof(ItemCount));
        OnPropertyChanged(nameof(ApproxTotal));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(IsCartEmpty));
        SaveLocalDraft();
    }

    private void OnLineChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CartItemViewModel.Quantity) or nameof(CartItemViewModel.Notes))
        {
            OnCartChanged();
        }
    }

    private void SaveLocalDraft()
    {
        if (_restoring || _context.TableId == 0)
        {
            return;
        }

        if (Cart.Count == 0 && _pendingKey is null)
        {
            _drafts.Delete(_context.TableId);
            return;
        }

        _drafts.Save(new LocalDraft
        {
            TableId = _context.TableId,
            TableCode = _context.TableCode,
            Mode = _context.Mode,
            OrderId = _context.OrderId,
            OrderNumber = _context.OrderNumber,
            GuestCount = GuestCount,
            PendingKey = _pendingKey,
            Lines = Cart.Select(c => c.ToLine()).ToList(),
        });
    }

    private void OnMenuChanged(object? sender, EventArgs e) => BuildMenu();

    private void BuildMenu()
    {
        var menu = _menuCache.Menu;
        var selectedId = SelectedCategory?.Id;
        Categories.Clear();
        foreach (var category in menu?.Categories ?? Array.Empty<MenuCategoryDto>())
        {
            Categories.Add(category);
        }

        SelectedCategory = Categories.FirstOrDefault(c => c.Id == selectedId) ?? Categories.FirstOrDefault();
        ShowItems();
    }

    private void ShowItems()
    {
        Items.Clear();
        if (_menuCache.Menu is not { } menu || SelectedCategory is null)
        {
            return;
        }

        foreach (var item in menu.Items.Where(i => i.CategoryId == SelectedCategory.Id))
        {
            Items.Add(new MenuTileViewModel(item, $"{_currency}{item.Price.ToString("N2", CultureInfo.CurrentCulture)}"));
        }
    }
}
