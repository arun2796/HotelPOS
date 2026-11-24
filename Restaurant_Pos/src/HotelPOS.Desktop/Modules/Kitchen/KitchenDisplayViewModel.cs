using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Kitchen;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Desktop.Modules.Common;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Configuration;
using HotelPOS.Desktop.Services.Menu;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Realtime;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Modules.Kitchen;

public enum TicketUrgency
{
    Normal,
    Warn,
    Late,
}

public sealed partial class TicketLine : ObservableObject
{
    public TicketLine(string text, string? notes, string modifiers, bool isCancelled, string? imageUrl = null)
    {
        Text = text;
        Notes = notes;
        Modifiers = modifiers;
        IsCancelled = isCancelled;
        ImageUrl = imageUrl;
    }

    public string Text { get; }

    public string? Notes { get; }

    public string Modifiers { get; }

    public bool IsCancelled { get; }

    // The menu item's picture URL from the ticket; the file comes from the image cache once pictures are switched on.
    public string? ImageUrl { get; }

    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);

    public bool HasModifiers => Modifiers.Length > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPicture))]
    private string? _imageFile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPicture))]
    private bool _showPicture;

    public bool HasPicture => ShowPicture && ImageFile is not null;
}

public sealed record StationFilter(int? Id, string Name);

public sealed partial class TicketCardViewModel : ObservableObject
{
    private readonly Func<string?, Task<string?>>? _imageLoader;
    private bool _showPictures;
    private IReadOnlyList<TicketLine> _lines = Array.Empty<TicketLine>();

    public TicketCardViewModel(KitchenTicketDto ticket, bool showPictures = false, Func<string?, Task<string?>>? imageLoader = null)
    {
        _ticket = ticket;
        _showPictures = showPictures;
        _imageLoader = imageLoader;
        RebuildLines();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Lines), nameof(ShowAccept), nameof(ShowStart), nameof(ShowReady), nameof(ShowDone), nameof(ShowRecall),
        nameof(IsAddBatch), nameof(StatusText))]
    private KitchenTicketDto _ticket;

    [ObservableProperty]
    private string _elapsedText = string.Empty;

    [ObservableProperty]
    private TicketUrgency _urgency;

    [ObservableProperty]
    private bool _isHighlighted;

    [ObservableProperty]
    private bool _isBusy;

    public int Id => Ticket.Id;

    public OrderType OrderType => Ticket.OrderType;

    public bool IsTakeaway => Ticket.OrderType == OrderType.Takeaway;

    // Each seat group has tickets of its own: "Seat A+B" next to the table.
    public bool HasSeatGroup => !string.IsNullOrWhiteSpace(Ticket.SeatCode);

    public string SeatText => HasSeatGroup ? "Seat " + Ticket.SeatCode : string.Empty;

    public bool IsDelivery => Ticket.OrderType == OrderType.Delivery;

    public bool HasDeliveryInfo => IsDelivery && (!string.IsNullOrWhiteSpace(Ticket.CustomerName) || !string.IsNullOrWhiteSpace(Ticket.DeliveryAddress));

    public string DeliveryRecipient => string.Join(" · ",
        new[] { Ticket.CustomerName, Ticket.CustomerPhone }.Where(s => !string.IsNullOrWhiteSpace(s)));

    public string DeliveryAddress => Ticket.DeliveryAddress ?? string.Empty;

    public bool IsAddBatch => Ticket.BatchNumber > 1;

    public string StatusText => Ticket.Status.ToString();

    public bool ShowAccept => Ticket.Status == KitchenOrderStatus.New;

    public bool ShowStart => Ticket.Status is KitchenOrderStatus.New or KitchenOrderStatus.Accepted;

    public bool ShowReady => Ticket.Status == KitchenOrderStatus.Preparing;

    public bool ShowDone => Ticket.Status == KitchenOrderStatus.Ready;

    public bool ShowRecall => Ticket.Status == KitchenOrderStatus.Ready;

    public IReadOnlyList<TicketLine> Lines => _lines;

    public bool ShowPictures
    {
        get => _showPictures;
        set
        {
            if (_showPictures == value)
            {
                return;
            }

            _showPictures = value;
            foreach (var line in _lines)
            {
                line.ShowPicture = value && line.ImageUrl is not null;
            }

            if (value)
            {
                LoadImages();
            }
        }
    }

    partial void OnTicketChanged(KitchenTicketDto value) => RebuildLines();

    private void RebuildLines()
    {
        _lines = Ticket.Items
            .Select(i => new TicketLine($"{i.Quantity} × {i.Name}", i.Notes, string.Join(", ", i.Modifiers), i.IsCancelled, i.ImageUrl))
            .ToList();
        foreach (var line in _lines)
        {
            line.ShowPicture = _showPictures && line.ImageUrl is not null;
        }

        if (_showPictures)
        {
            LoadImages();
        }
    }

    // Authenticated download/cache is the menu cache's (the order builder's), never a loader of our own.
    private void LoadImages()
    {
        if (_imageLoader is null)
        {
            return;
        }

        foreach (var line in _lines.Where(l => l.ImageUrl is not null && l.ImageFile is null))
        {
            _ = LoadImageAsync(line);
        }
    }

    private async Task LoadImageAsync(TicketLine line)
    {
        try
        {
            line.ImageFile = await _imageLoader!(line.ImageUrl);
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.Net.Http.HttpRequestException or OperationCanceledException)
        {
            // A picture that cannot be fetched leaves the line text-only.
        }
    }

    public void Tick(DateTime serverNowUtc, int warnMinutes, int lateMinutes)
    {
        var since = Ticket.Status == KitchenOrderStatus.Ready && Ticket.ReadyAtUtc is { } ready ? ready : Ticket.CreatedAtUtc;
        var minutes = Math.Max(0, (int)(serverNowUtc - since).TotalMinutes);
        ElapsedText = minutes < 60 ? $"{minutes} min" : string.Create(CultureInfo.CurrentCulture, $"{minutes / 60} h {minutes % 60:00}");
        Urgency = Ticket.Status == KitchenOrderStatus.Ready ? TicketUrgency.Normal
            : minutes >= lateMinutes ? TicketUrgency.Late
            : minutes >= warnMinutes ? TicketUrgency.Warn
            : TicketUrgency.Normal;
    }
}

public sealed partial class SoldOutItemViewModel : ObservableObject
{
    public SoldOutItemViewModel(int id, string name, bool isAvailable, StockTrackingMode stockMode = StockTrackingMode.None, int? stockRemaining = null)
    {
        Id = id;
        Name = name;
        _isAvailable = isAvailable;
        _stockMode = stockMode;
        _stockRemaining = stockRemaining;
    }

    public int Id { get; }

    public string Name { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActionText), nameof(StockText))]
    private bool _isAvailable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StockText), nameof(HasStockCount))]
    private StockTrackingMode _stockMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StockText))]
    private int? _stockRemaining;

    // What the supervisor types before pressing SET COUNT / APPROX. / + ADD.
    [ObservableProperty]
    private string _stockInput = string.Empty;

    // Why a waste or correction is made; the ledger keeps it.
    [ObservableProperty]
    private string _reasonInput = string.Empty;

    // The count at or below which the item shows as Low stock on every screen.
    [ObservableProperty]
    private string _reorderInput = string.Empty;

    public string ActionText => IsAvailable ? "Mark sold out" : "Available again";

    public bool HasStockCount => StockMode != StockTrackingMode.None;

    public string StockText
    {
        get
        {
            if (StockMode == StockTrackingMode.None || StockRemaining is not { } left)
            {
                return IsAvailable ? "No count" : "Sold out";
            }

            return StockMode == StockTrackingMode.Approximate
                ? string.Create(CultureInfo.CurrentCulture, $"Approx. {left} left")
                : string.Create(CultureInfo.CurrentCulture, $"{left} left");
        }
    }

    public void Apply(bool isAvailable, StockTrackingMode stockMode, int? stockRemaining)
    {
        IsAvailable = isAvailable;
        StockMode = stockMode;
        StockRemaining = stockRemaining;
    }
}

public sealed partial class KitchenDisplayViewModel : ViewModelBase, INavigationAware, IRefreshable, IDisposable
{
    public const int DefaultWarnMinutes = 10;
    public const int DefaultLateMinutes = 20;

    private static readonly TimeSpan HighlightFor = TimeSpan.FromSeconds(5);

    private readonly IKitchenApi _kitchenApi;
    private readonly IMenuApi _menuApi;
    private readonly IMenuCache _menuCache;
    private readonly ISystemApi _systemApi;
    private readonly IRealtimeClient _realtime;
    private readonly IClientSettingsService _settings;
    private readonly INotificationRouter _router;
    private readonly INotificationService _notifications;
    private readonly List<IDisposable> _subscriptions = new();
    private CancellationTokenSource? _timer;
    private TimeSpan _serverClockOffset;
    private bool _loadingStations;
    private bool _showPictures;
    private bool _listeningToMenu;
    private const int MaxStockCount = 100000;

    public KitchenDisplayViewModel(
        IKitchenApi kitchenApi,
        IMenuApi menuApi,
        IMenuCache menuCache,
        ISystemApi systemApi,
        IRealtimeClient realtime,
        IClientSettingsService settings,
        INotificationRouter router,
        INotificationService notifications)
    {
        _kitchenApi = kitchenApi;
        _menuApi = menuApi;
        _menuCache = menuCache;
        _systemApi = systemApi;
        _realtime = realtime;
        _settings = settings;
        _router = router;
        _notifications = notifications;
        _showPictures = settings.Current.ShowTicketPictures;
    }

    public ObservableCollection<TicketCardViewModel> NewTickets { get; } = new();

    public ObservableCollection<TicketCardViewModel> PreparingTickets { get; } = new();

    public ObservableCollection<TicketCardViewModel> ReadyTickets { get; } = new();

    public ObservableCollection<StationFilter> Stations { get; } = new();

    public ObservableCollection<SoldOutItemViewModel> MenuItems { get; } = new();

    [ObservableProperty]
    private StationFilter? _selectedStation;

    [ObservableProperty]
    private bool _isSoldOutOpen;

    // Phase 22 P5: "Show item pictures", a per-terminal setting kept with the station choice; off by default.
    public bool ShowPictures
    {
        get => _showPictures;
        set
        {
            if (_showPictures == value)
            {
                return;
            }

            _showPictures = value;
            var settings = _settings.Current;
            settings.ShowTicketPictures = value;
            _settings.Save(settings);
            foreach (var card in AllCards)
            {
                card.ShowPictures = value;
            }

            OnPropertyChanged();
        }
    }

    private TicketCardViewModel NewCard(KitchenTicketDto ticket) => new(ticket, _showPictures, _menuCache.GetImageFileAsync);

    public int WarnMinutes { get; private set; } = DefaultWarnMinutes;

    public int LateMinutes { get; private set; } = DefaultLateMinutes;

    public string NewHeader => $"NEW · {NewTickets.Count}";

    public string PreparingHeader => $"PREPARING · {PreparingTickets.Count}";

    public string ReadyHeader => $"READY · {ReadyTickets.Count}";

    public DateTime ServerNowUtc => DateTime.UtcNow + _serverClockOffset;

    public IEnumerable<TicketCardViewModel> AllCards => NewTickets.Concat(PreparingTickets).Concat(ReadyTickets);

    public async Task OnNavigatedToAsync(object? parameter)
    {
        var settings = await _systemApi.GetPublicSettingsAsync();
        WarnMinutes = ReadInt(settings.Data, SettingKeys.KitchenWarnMinutes, DefaultWarnMinutes);
        LateMinutes = Math.Max(WarnMinutes, ReadInt(settings.Data, SettingKeys.KitchenLateMinutes, DefaultLateMinutes));

        await LoadStationsAsync();
        _subscriptions.Add(_realtime.Subscribe<KitchenTicketCreatedEvent>(HubEvents.KitchenTicketCreated, e => _ = OnTicketEventAsync(e.TicketId, e.StationId, isNew: true)));
        _subscriptions.Add(_realtime.Subscribe<KitchenTicketUpdatedEvent>(HubEvents.KitchenTicketUpdated, e => _ = OnTicketEventAsync(e.TicketId, e.StationId, isNew: false)));
        await _realtime.SetStationAsync(SelectedStation?.Id);
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
        StopListeningToMenu();
        _timer?.Cancel();
        _timer?.Dispose();
        _timer = null;
        _ = _realtime.SetStationAsync(null);
    }

    public async Task RefreshAsync()
    {
        var result = await _kitchenApi.GetOpenAsync(SelectedStation?.Id);
        if (!result.Success || result.Data is null)
        {
            ErrorMessage = result.IsConnectionFailure
                ? "Connection unavailable. Tickets will refresh when the server is back."
                : result.Message;
            return;
        }

        ErrorMessage = null;
        _serverClockOffset = result.Data.ServerTimeUtc - DateTime.UtcNow;
        NewTickets.Clear();
        PreparingTickets.Clear();
        ReadyTickets.Clear();
        foreach (var ticket in result.Data.Tickets)
        {
            Place(NewCard(ticket));
        }

        UpdateHeaders();
    }

    public void Upsert(KitchenTicketDto ticket, bool isNew)
    {
        var card = AllCards.FirstOrDefault(c => c.Id == ticket.Id);
        if (card is not null)
        {
            Column(card.Ticket.Status)?.Remove(card);
            card.Ticket = ticket;
        }
        else if (KitchenOpen(ticket.Status))
        {
            card = NewCard(ticket);
            if (isNew)
            {
                _ = HighlightAsync(card);
            }
        }

        if (card is not null && KitchenOpen(ticket.Status))
        {
            Place(card);
        }

        UpdateHeaders();
    }

    partial void OnSelectedStationChanged(StationFilter? value)
    {
        if (_loadingStations)
        {
            return;
        }

        var settings = _settings.Current;
        settings.StationId = value?.Id;
        _settings.Save(settings);
        _ = _realtime.SetStationAsync(value?.Id);
        _ = RefreshAsync();
    }

    [RelayCommand]
    private Task RefreshListAsync() => RefreshAsync();

    [RelayCommand]
    private Task AcceptAsync(TicketCardViewModel? card) => ActAsync(card, KitchenActions.Accept);

    [RelayCommand]
    private Task StartAsync(TicketCardViewModel? card) => ActAsync(card, KitchenActions.Start);

    [RelayCommand]
    private Task ReadyAsync(TicketCardViewModel? card) => ActAsync(card, KitchenActions.Ready);

    [RelayCommand]
    private Task DoneAsync(TicketCardViewModel? card) => ActAsync(card, KitchenActions.Complete);

    [RelayCommand]
    private Task RecallAsync(TicketCardViewModel? card) => ActAsync(card, KitchenActions.Recall);

    [RelayCommand]
    private async Task OpenSoldOutAsync()
    {
        if (_menuCache.Menu is null)
        {
            await _menuCache.RefreshAsync();
        }

        SyncMenuItems(rebuild: true);
        if (!_listeningToMenu)
        {
            _menuCache.Changed += OnMenuCacheChanged;
            _listeningToMenu = true;
        }

        IsSoldOutOpen = true;
    }

    [RelayCommand]
    private void CloseSoldOut()
    {
        IsSoldOutOpen = false;
        StopListeningToMenu();
    }

    private void OnMenuCacheChanged(object? sender, EventArgs e) => SyncMenuItems(rebuild: false);

    private void StopListeningToMenu()
    {
        if (_listeningToMenu)
        {
            _menuCache.Changed -= OnMenuCacheChanged;
            _listeningToMenu = false;
        }
    }

    // Fills the availability/stock list from the menu cache. A cache change updates the rows in place so a half-typed count is kept.
    private void SyncMenuItems(bool rebuild)
    {
        var entries = (_menuCache.Menu?.Items ?? Array.Empty<Contracts.Menu.MenuEntryDto>()).OrderBy(i => i.Name, StringComparer.CurrentCulture).ToList();
        var sameItems = !rebuild && entries.Count == MenuItems.Count && entries.Select(e => e.Id).SequenceEqual(MenuItems.Select(m => m.Id));
        if (sameItems)
        {
            for (var i = 0; i < entries.Count; i++)
            {
                MenuItems[i].Apply(entries[i].IsAvailable, entries[i].StockMode, entries[i].StockRemaining);
            }

            return;
        }

        var typed = MenuItems.ToDictionary(m => m.Id, m => m.StockInput);
        MenuItems.Clear();
        foreach (var item in entries)
        {
            var row = new SoldOutItemViewModel(item.Id, item.Name, item.IsAvailable, item.StockMode, item.StockRemaining);
            if (!rebuild && typed.TryGetValue(item.Id, out var input))
            {
                row.StockInput = input;
            }

            MenuItems.Add(row);
        }
    }

    [RelayCommand]
    private Task SetStockCountAsync(SoldOutItemViewModel? item) => ChangeStockAsync(item, StockTrackingMode.Exact, add: false);

    [RelayCommand]
    private Task SetStockApproximateAsync(SoldOutItemViewModel? item) => ChangeStockAsync(item, StockTrackingMode.Approximate, add: false);

    [RelayCommand]
    private Task AddStockAsync(SoldOutItemViewModel? item)
        => ChangeStockAsync(item, item is { StockMode: not StockTrackingMode.None } ? item.StockMode : StockTrackingMode.Exact, add: true);

    [RelayCommand]
    private Task ClearStockCountAsync(SoldOutItemViewModel? item) => ChangeStockAsync(item, StockTrackingMode.None, add: false);

    // Waste: prepared food thrown away. Adjust: a counted correction (a minus takes off, a plus adds). Both need a reason.
    [RelayCommand]
    private Task WasteStockAsync(SoldOutItemViewModel? item) => AdjustStockAsync(item, StockMovementType.Waste);

    [RelayCommand]
    private Task CorrectStockAsync(SoldOutItemViewModel? item) => AdjustStockAsync(item, StockMovementType.Adjustment);

    private async Task AdjustStockAsync(SoldOutItemViewModel? item, StockMovementType kind)
    {
        if (item is null)
        {
            return;
        }

        var styles = kind == StockMovementType.Waste ? NumberStyles.None : NumberStyles.AllowLeadingSign;
        if (!int.TryParse(item.StockInput?.Trim(), styles, CultureInfo.InvariantCulture, out var quantity) || quantity == 0 || Math.Abs(quantity) > MaxStockCount)
        {
            _notifications.Error(kind == StockMovementType.Waste
                ? $"Enter how many {item.Name} were lost (a whole number)."
                : $"Enter the change for {item.Name}, for example -3 or 5.");
            return;
        }

        var reason = item.ReasonInput?.Trim() ?? string.Empty;
        if (reason.Length == 0)
        {
            _notifications.Error("Say why (for example: dropped, expired, counted again).");
            return;
        }

        var result = await _menuApi.AdjustStockAsync(item.Id, new HotelPOS.Contracts.Menu.AdjustStockRequest { Kind = kind, Quantity = quantity, Reason = reason });
        if (result.Success && result.Data is not null)
        {
            item.Apply(result.Data.IsAvailable, result.Data.StockMode, result.Data.StockRemaining);
            item.StockInput = string.Empty;
            item.ReasonInput = string.Empty;
            _notifications.Success(kind == StockMovementType.Waste
                ? $"{item.Name}: {quantity} written off, {item.StockText}."
                : $"{item.Name}: corrected by {quantity:+0;-0}, {item.StockText}.");
        }
        else
        {
            _notifications.Error(ApiFailures.Describe(result), result.CorrelationId);
        }
    }

    [RelayCommand]
    private async Task SetReorderLevelAsync(SoldOutItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        int? level = null;
        if (item.ReorderInput?.Trim() is { Length: > 0 } text)
        {
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed > MaxStockCount)
            {
                _notifications.Error($"Enter a whole number from 0 to {MaxStockCount}.");
                return;
            }

            level = parsed;
        }

        var result = await _menuApi.SetReorderLevelAsync(item.Id, new HotelPOS.Contracts.Menu.SetReorderLevelRequest { ReorderLevel = level });
        if (result.Success)
        {
            item.ReorderInput = string.Empty;
            _notifications.Success(level is null or 0
                ? $"{item.Name}: low-stock warning back to the default."
                : $"{item.Name}: shows as low stock at {level} or fewer.");
        }
        else
        {
            _notifications.Error(ApiFailures.Describe(result), result.CorrelationId);
        }
    }

    // The ledger of an item: every change of its count with who, why and for which order.
    public ObservableCollection<StockHistoryRow> History { get; } = new();

    [ObservableProperty]
    private bool _isHistoryOpen;

    [ObservableProperty]
    private string _historyTitle = string.Empty;

    [ObservableProperty]
    private string _historySummary = string.Empty;

    [RelayCommand]
    private async Task ShowStockHistoryAsync(SoldOutItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        var result = await _menuApi.GetStockHistoryAsync(item.Id);
        if (!result.Success || result.Data is null)
        {
            _notifications.Error(ApiFailures.Describe(result), result.CorrelationId);
            return;
        }

        History.Clear();
        foreach (var movement in result.Data.Movements)
        {
            History.Add(new StockHistoryRow(movement));
        }

        HistoryTitle = $"{result.Data.ItemName} · stock history";
        HistorySummary = $"Today: {result.Data.Sold} sold, {result.Data.Refilled} refilled, {result.Data.Wasted} wasted"
            + (result.Data.Remaining is { } left ? $" · {left} left now" : string.Empty);
        IsHistoryOpen = true;
    }

    [RelayCommand]
    private void CloseHistory() => IsHistoryOpen = false;

    private async Task ChangeStockAsync(SoldOutItemViewModel? item, StockTrackingMode mode, bool add)
    {
        if (item is null)
        {
            return;
        }

        int? quantity = null;
        if (mode != StockTrackingMode.None)
        {
            if (!int.TryParse(item.StockInput?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed < 0 || parsed > MaxStockCount)
            {
                _notifications.Error($"Enter a whole number from 0 to {MaxStockCount} for {item.Name}.");
                return;
            }

            if (add && parsed == 0)
            {
                _notifications.Error($"Enter how many to add for {item.Name}.");
                return;
            }

            quantity = parsed;
        }

        var result = await _menuApi.SetStockAsync(item.Id, new Contracts.Menu.SetStockRequest { Mode = mode, Quantity = quantity, Add = add });
        if (result.Success && result.Data is not null)
        {
            item.Apply(result.Data.IsAvailable, result.Data.StockMode, result.Data.StockRemaining);
            item.StockInput = string.Empty;
            _notifications.Success(mode == StockTrackingMode.None
                ? $"{item.Name}: stock count switched off."
                : add
                    ? $"{item.Name}: {quantity} added, {item.StockText}."
                    : $"{item.Name}: stock set to {quantity}.");
        }
        else
        {
            _notifications.Error(ApiFailures.Describe(result), result.CorrelationId);
        }
    }

    [RelayCommand]
    private async Task ToggleAvailabilityAsync(SoldOutItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        var result = await _menuApi.SetAvailabilityAsync(item.Id, !item.IsAvailable);
        if (result.Success && result.Data is not null)
        {
            item.IsAvailable = result.Data.IsAvailable;
            _notifications.Success(item.IsAvailable ? $"{item.Name} is available again." : $"{item.Name} is sold out.");
        }
        else
        {
            _notifications.Error(ApiFailures.Describe(result), result.CorrelationId);
        }
    }

    private async Task ActAsync(TicketCardViewModel? card, string action)
    {
        if (card is null || card.IsBusy)
        {
            return;
        }

        card.IsBusy = true;
        try
        {
            var result = await _kitchenApi.ActAsync(card.Id, action);
            if (result.Success && result.Data is not null)
            {
                Upsert(result.Data, isNew: false);
                return;
            }

            if (result.Data is not null)
            {
                Upsert(result.Data, isNew: false);
            }

            _notifications.Error(ApiFailures.Describe(result), result.CorrelationId);
        }
        finally
        {
            card.IsBusy = false;
        }
    }

    private async Task OnTicketEventAsync(int ticketId, int stationId, bool isNew)
    {
        if (SelectedStation?.Id is { } station && station != stationId)
        {
            return;
        }

        // Events only say "something changed": the ticket is read again, so a duplicate event changes nothing.
        var result = await _kitchenApi.GetAsync(ticketId);
        if (result.Success && result.Data is not null)
        {
            var known = AllCards.Any(c => c.Id == ticketId);
            // The sound and toast for a new ticket come from NotificationRouter (KitchenTicketCreated), per terminal preference.
            Upsert(result.Data, isNew && !known);
        }
    }

    private async Task LoadStationsAsync()
    {
        _loadingStations = true;
        try
        {
            Stations.Clear();
            Stations.Add(new StationFilter(null, "All stations"));
            var result = await _menuApi.GetStationsAsync(includeInactive: false);
            foreach (var station in result.Data ?? new List<Contracts.Menu.StationDto>())
            {
                Stations.Add(new StationFilter(station.Id, station.Name));
            }

            var saved = _settings.Current.StationId;
            SelectedStation = Stations.FirstOrDefault(s => s.Id == saved) ?? Stations[0];
        }
        finally
        {
            _loadingStations = false;
        }
    }

    private void Place(TicketCardViewModel card)
    {
        var column = Column(card.Ticket.Status);
        if (column is null)
        {
            return;
        }

        var index = 0;
        while (index < column.Count && column[index].Ticket.CreatedAtUtc <= card.Ticket.CreatedAtUtc)
        {
            index++;
        }

        column.Insert(index, card);
        card.Tick(ServerNowUtc, WarnMinutes, LateMinutes);
    }

    private ObservableCollection<TicketCardViewModel>? Column(KitchenOrderStatus status) => status switch
    {
        KitchenOrderStatus.New or KitchenOrderStatus.Accepted => NewTickets,
        KitchenOrderStatus.Preparing => PreparingTickets,
        KitchenOrderStatus.Ready => ReadyTickets,
        _ => null,
    };

    private static bool KitchenOpen(KitchenOrderStatus status) => status is not (KitchenOrderStatus.Completed or KitchenOrderStatus.Cancelled);

    private void UpdateHeaders()
    {
        OnPropertyChanged(nameof(NewHeader));
        OnPropertyChanged(nameof(PreparingHeader));
        OnPropertyChanged(nameof(ReadyHeader));
    }

    private static async Task HighlightAsync(TicketCardViewModel card)
    {
        card.IsHighlighted = true;
        await Task.Delay(HighlightFor);
        card.IsHighlighted = false;
    }

    // Re-colours every card; a card that was not late before this tick and is now raises one TicketDelayed notification.
    // Cards already late when first shown (a reload, a restart) were ticked on placement, so they never announce.
    internal void TickCards()
    {
        foreach (var card in AllCards)
        {
            var wasLate = card.Urgency == TicketUrgency.Late;
            card.Tick(ServerNowUtc, WarnMinutes, LateMinutes);
            if (!wasLate && card.Urgency == TicketUrgency.Late)
            {
                _router.TicketDelayed(card.Id, card.Ticket.TicketNumber, card.Ticket.TableCode);
            }
        }
    }

    private async Task RunTimerAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        var ticks = 0;
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                TickCards();

                // A full reload every minute repairs anything a lost event left behind.
                if (++ticks % 4 == 0)
                {
                    await RefreshAsync();
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static int ReadInt(IEnumerable<SettingDto>? settings, string key, int fallback) =>
        int.TryParse(settings?.FirstOrDefault(s => s.Key == key)?.Value, out var value) && value > 0 ? value : fallback;
}
