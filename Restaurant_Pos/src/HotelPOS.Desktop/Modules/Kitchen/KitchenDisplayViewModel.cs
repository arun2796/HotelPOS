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

public sealed record TicketLine(string Text, string? Notes, string Modifiers, bool IsCancelled)
{
    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);

    public bool HasModifiers => Modifiers.Length > 0;
}

public sealed record StationFilter(int? Id, string Name);

public sealed partial class TicketCardViewModel : ObservableObject
{
    public TicketCardViewModel(KitchenTicketDto ticket)
    {
        _ticket = ticket;
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

    public bool IsAddBatch => Ticket.BatchNumber > 1;

    public string StatusText => Ticket.Status.ToString();

    public bool ShowAccept => Ticket.Status == KitchenOrderStatus.New;

    public bool ShowStart => Ticket.Status is KitchenOrderStatus.New or KitchenOrderStatus.Accepted;

    public bool ShowReady => Ticket.Status == KitchenOrderStatus.Preparing;

    public bool ShowDone => Ticket.Status == KitchenOrderStatus.Ready;

    public bool ShowRecall => Ticket.Status == KitchenOrderStatus.Ready;

    public IReadOnlyList<TicketLine> Lines => Ticket.Items
        .Select(i => new TicketLine($"{i.Quantity} × {i.Name}", i.Notes, string.Join(", ", i.Modifiers), i.IsCancelled))
        .ToList();

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
    public SoldOutItemViewModel(int id, string name, bool isAvailable)
    {
        Id = id;
        Name = name;
        _isAvailable = isAvailable;
    }

    public int Id { get; }

    public string Name { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActionText))]
    private bool _isAvailable;

    public string ActionText => IsAvailable ? "Mark sold out" : "Available again";
}

public sealed partial class KitchenDisplayViewModel : ObservableObject, INavigationAware, IRefreshable, IDisposable
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
    private readonly ISoundPlayer _sound;
    private readonly INotificationService _notifications;
    private readonly List<IDisposable> _subscriptions = new();
    private CancellationTokenSource? _timer;
    private TimeSpan _serverClockOffset;
    private bool _loadingStations;

    public KitchenDisplayViewModel(
        IKitchenApi kitchenApi,
        IMenuApi menuApi,
        IMenuCache menuCache,
        ISystemApi systemApi,
        IRealtimeClient realtime,
        IClientSettingsService settings,
        ISoundPlayer sound,
        INotificationService notifications)
    {
        _kitchenApi = kitchenApi;
        _menuApi = menuApi;
        _menuCache = menuCache;
        _systemApi = systemApi;
        _realtime = realtime;
        _settings = settings;
        _sound = sound;
        _notifications = notifications;
    }

    public ObservableCollection<TicketCardViewModel> NewTickets { get; } = new();

    public ObservableCollection<TicketCardViewModel> PreparingTickets { get; } = new();

    public ObservableCollection<TicketCardViewModel> ReadyTickets { get; } = new();

    public ObservableCollection<StationFilter> Stations { get; } = new();

    public ObservableCollection<SoldOutItemViewModel> MenuItems { get; } = new();

    [ObservableProperty]
    private StationFilter? _selectedStation;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isSoldOutOpen;

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
            Place(new TicketCardViewModel(ticket));
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
            card = new TicketCardViewModel(ticket);
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

        MenuItems.Clear();
        foreach (var item in (_menuCache.Menu?.Items ?? Array.Empty<Contracts.Menu.MenuEntryDto>()).OrderBy(i => i.Name, StringComparer.CurrentCulture))
        {
            MenuItems.Add(new SoldOutItemViewModel(item.Id, item.Name, item.IsAvailable));
        }

        IsSoldOutOpen = true;
    }

    [RelayCommand]
    private void CloseSoldOut() => IsSoldOutOpen = false;

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
            Upsert(result.Data, isNew && !known);
            if (isNew && !known)
            {
                _sound.NewTicket();
            }
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

    private async Task RunTimerAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        var ticks = 0;
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                foreach (var card in AllCards)
                {
                    card.Tick(ServerNowUtc, WarnMinutes, LateMinutes);
                }

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
