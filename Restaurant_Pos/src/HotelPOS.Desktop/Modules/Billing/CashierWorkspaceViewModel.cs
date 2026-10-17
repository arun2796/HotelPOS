using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Desktop.Modules.Common;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Configuration;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Realtime;
using HotelPOS.Desktop.Services.Ui;
using Microsoft.Extensions.DependencyInjection;

namespace HotelPOS.Desktop.Modules.Billing;

/// <summary>Queue chips. Waiting = open bills, Invoiced = finalized and unpaid, Partly paid = finalized with a payment, Paid = today's closed bills.</summary>
public enum QueueFilter
{
    All,
    Waiting,
    Invoiced,
    PartlyPaid,
    Paid,
}

public sealed partial class QueueFilterChip : ObservableObject
{
    public QueueFilterChip(QueueFilter filter, string label)
    {
        Filter = filter;
        Label = label;
    }

    public QueueFilter Filter { get; }

    public string Label { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCount))]
    private int? _count;

    [ObservableProperty]
    private bool _isSelected;

    public bool HasCount => Count is > 0;
}

/// <summary>
/// Phase 21.3: the cashier's three-pane workspace. Left: the bill queue with search, sort and status chips; centre and
/// right: the selected bill, hosted as the existing <see cref="BillDetailViewModel"/> (its bill, payment, idempotency
/// and permission logic are unchanged; only the surface is new). The detail's "back to the queue" navigation is
/// intercepted so that it clears the selection here instead of leaving the workspace.
/// </summary>
public sealed partial class CashierWorkspaceViewModel : ViewModelBase, INavigationAware, IRefreshable, IDisposable
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(30);

    private readonly IBillingApi _billingApi;
    private readonly IRealtimeClient _realtime;
    private readonly IClientSettingsService _settings;
    private readonly INotificationService _notifications;
    private readonly INavigationService _navigation;
    private readonly IServiceProvider _services;
    private readonly List<IDisposable> _subscriptions = new();
    private List<BillQueueItemViewModel> _pending = new();
    private List<BillQueueItemViewModel>? _closed;
    private CancellationTokenSource? _timer;
    private bool _rebuilding;
    private int _selecting;

    public CashierWorkspaceViewModel(
        IBillingApi billingApi,
        IRealtimeClient realtime,
        IClientSettingsService settings,
        INotificationService notifications,
        INavigationService navigation,
        IServiceProvider services)
    {
        _billingApi = billingApi;
        _realtime = realtime;
        _settings = settings;
        _notifications = notifications;
        _navigation = navigation;
        _services = services;
        Chips = new[]
        {
            new QueueFilterChip(QueueFilter.All, "All"),
            new QueueFilterChip(QueueFilter.Waiting, "Waiting"),
            new QueueFilterChip(QueueFilter.Invoiced, "Invoiced"),
            new QueueFilterChip(QueueFilter.PartlyPaid, "Partly paid"),
            new QueueFilterChip(QueueFilter.Paid, "Paid"),
        };
        Chips[0].IsSelected = true;
    }

    /// <summary>The bills that match the chip, the search and the sort.</summary>
    public ObservableCollection<BillQueueItemViewModel> Bills { get; } = new();

    public IReadOnlyList<QueueFilterChip> Chips { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPaidFilter), nameof(EmptyTitle), nameof(EmptyMessage))]
    private QueueFilter _filter;

    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>0 = oldest request first (the default: nobody waits longer than needed), 1 = highest amount first.</summary>
    [ObservableProperty]
    private int _sortIndex;

    [ObservableProperty]
    private BillQueueItemViewModel? _selectedBill;

    /// <summary>The open bill, or null while nothing is selected. Created per selection so each bill starts clean.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private BillDetailViewModel? _detail;

    public bool HasSelection => Detail is not null;

    public bool IsPaidFilter => Filter == QueueFilter.Paid;

    public bool SortByAmount => SortIndex == 1;

    public bool IsEmpty => Bills.Count == 0 && ErrorMessage is null;

    public string Header => _pending.Count == 1 ? "1 bill waiting" : $"{_pending.Count} bills waiting";

    public string EmptyTitle => Filter switch
    {
        QueueFilter.Paid => "No bills paid today",
        QueueFilter.All when string.IsNullOrWhiteSpace(SearchText) => "No bills waiting",
        _ => "No matching bills",
    };

    public string EmptyMessage => Filter switch
    {
        QueueFilter.Paid => "Settled bills appear here; reprints and refunds are under Closed Bills.",
        QueueFilter.All when string.IsNullOrWhiteSpace(SearchText) => "New bill requests from waiters appear here instantly.",
        _ => "Try another chip or clear the search.",
    };

    public async Task OnNavigatedToAsync(object? parameter)
    {
        _subscriptions.Add(_realtime.Subscribe<BillRequestedEvent>(HubEvents.BillRequested, notice => _ = RefreshAsync()));
        _subscriptions.Add(_realtime.Subscribe<BillUpdatedEvent>(HubEvents.BillUpdated, notice => _ = RefreshAsync()));
        _subscriptions.Add(_realtime.Subscribe<PaymentCompletedEvent>(HubEvents.PaymentCompleted, notice => _ = RefreshAsync()));
        await RefreshAsync();
        if (parameter is int billId)
        {
            // Opened with a bill (command palette, a notification): select it, loading it even when it is not in the queue.
            await SelectAsync(Bills.FirstOrDefault(b => b.Id == billId) ?? _pending.FirstOrDefault(b => b.Id == billId), billId);
        }

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
        Detail?.Dispose();
        Detail = null;
    }

    public async Task RefreshAsync()
    {
        var result = await _billingApi.GetPendingAsync();
        if (!result.Success || result.Data is null)
        {
            ErrorMessage = result.IsConnectionFailure ? "Connection unavailable. Bills will refresh when the server is back." : result.Message;
            Notify();
            return;
        }

        ErrorMessage = null;
        var now = DateTime.UtcNow;
        _pending = result.Data.Select(bill =>
        {
            var item = new BillQueueItemViewModel(bill, _settings.Current.DeviceName);
            item.Tick(now);
            return item;
        }).ToList();

        if (_closed is not null || IsPaidFilter)
        {
            await LoadClosedAsync();
        }

        Rebuild();
    }

    private async Task LoadClosedAsync()
    {
        var closed = await _billingApi.GetClosedAsync(null, null);
        if (closed.Success && closed.Data is not null)
        {
            var now = DateTime.UtcNow;
            _closed = closed.Data.Select(bill =>
            {
                var item = new BillQueueItemViewModel(bill, _settings.Current.DeviceName);
                item.Tick(now);
                return item;
            }).ToList();
        }
        else if (IsPaidFilter)
        {
            ErrorMessage = closed.IsConnectionFailure ? "Connection unavailable." : closed.Message;
        }
    }

    [RelayCommand]
    private async Task SelectFilterAsync(QueueFilter filter)
    {
        Filter = filter;
        if (filter == QueueFilter.Paid && _closed is null)
        {
            await LoadClosedAsync();
        }

        Rebuild();
    }

    [RelayCommand]
    private Task RefreshListAsync() => RefreshAsync();

    partial void OnFilterChanged(QueueFilter value)
    {
        foreach (var chip in Chips)
        {
            chip.IsSelected = chip.Filter == value;
        }
    }

    partial void OnSearchTextChanged(string value) => Rebuild();

    partial void OnSortIndexChanged(int value)
    {
        OnPropertyChanged(nameof(SortByAmount));
        Rebuild();
    }

    partial void OnSelectedBillChanged(BillQueueItemViewModel? value)
    {
        if (!_rebuilding && _selecting == 0)
        {
            _ = SelectAsync(value);
        }
    }

    /// <summary>
    /// Opens a bill in the centre and payment columns: a pending bill is claimed first (one held elsewhere still opens,
    /// read-only, so the cashier sees who has it); a closed bill is only shown. Null clears the selection and releases
    /// the claim.
    /// </summary>
    public async Task SelectAsync(BillQueueItemViewModel? item, int? billId = null)
    {
        var id = item?.Id ?? billId;
        if (id is not null && Detail is { } open && open.Bill?.Id == id)
        {
            return;
        }

        _selecting++;
        try
        {
            SelectedBill = item;
            await CloseDetailAsync(releaseClaim: true);
            if (id is not { } target)
            {
                return;
            }

            var closed = item?.Bill.Status is BillStatus.Settled or BillStatus.Voided;
            if (!closed)
            {
                var claim = await _billingApi.ClaimAsync(target);
                if (!claim.Success && !claim.HasError(ErrorCodes.BillClaimed))
                {
                    _notifications.Error(ApiFailures.Describe(claim), claim.CorrelationId);
                    SelectedBill = null;
                    await RefreshAsync();
                    return;
                }
            }

            var detail = ActivatorUtilities.CreateInstance<BillDetailViewModel>(_services, new WorkspaceNavigation(this, _navigation));
            Detail = detail;
            await detail.OnNavigatedToAsync(target);
        }
        finally
        {
            _selecting--;
        }
    }

    [RelayCommand]
    private Task ClearSelectionAsync() => SelectAsync(null);

    // The detail asked to go back to the queue (Esc, Next bill, after a reopen or void): clear the selection here.
    private async Task OnDetailClosedAsync()
    {
        _selecting++;
        try
        {
            SelectedBill = null;
            await CloseDetailAsync(releaseClaim: false);
        }
        finally
        {
            _selecting--;
        }

        await RefreshAsync();
    }

    private async Task CloseDetailAsync(bool releaseClaim)
    {
        var previous = Detail;
        if (previous is null)
        {
            return;
        }

        Detail = null;
        previous.OnNavigatedFrom();
        if (releaseClaim && previous.Bill is { IsClaimedByMe: true, Status: BillStatus.Open or BillStatus.Finalized } bill)
        {
            await _billingApi.ReleaseAsync(bill.Id);
        }
    }

    private void Rebuild()
    {
        var source = Filter switch
        {
            QueueFilter.Paid => _closed ?? new List<BillQueueItemViewModel>(),
            QueueFilter.Waiting => _pending.Where(b => b.Bill.Status == BillStatus.Open),
            QueueFilter.Invoiced => _pending.Where(b => b.Bill.Status == BillStatus.Finalized && b.Bill.PaidAmount <= 0),
            QueueFilter.PartlyPaid => _pending.Where(b => b.Bill.Status == BillStatus.Finalized && b.Bill.PaidAmount > 0),
            _ => _pending,
        };

        var search = SearchText.Trim();
        if (search.Length > 0)
        {
            source = source.Where(b => Matches(b.Bill, search));
        }

        var ordered = IsPaidFilter
            ? source.OrderByDescending(b => b.Bill.SettledAtUtc ?? b.Bill.VoidedAtUtc ?? b.Bill.RequestedAtUtc)
            : SortByAmount
                ? source.OrderByDescending(b => b.Bill.GrandTotal - b.Bill.PaidAmount).ThenBy(b => b.Bill.RequestedAtUtc)
                : source.OrderBy(b => b.Bill.RequestedAtUtc);

        var keep = SelectedBill?.Id ?? Detail?.Bill?.Id;
        _rebuilding = true;
        try
        {
            Bills.Clear();
            foreach (var bill in ordered)
            {
                Bills.Add(bill);
            }

            SelectedBill = Bills.FirstOrDefault(b => b.Id == keep);
        }
        finally
        {
            _rebuilding = false;
        }

        Chips[1].Count = _pending.Count(b => b.Bill.Status == BillStatus.Open);
        Chips[2].Count = _pending.Count(b => b.Bill.Status == BillStatus.Finalized && b.Bill.PaidAmount <= 0);
        Chips[3].Count = _pending.Count(b => b.Bill.Status == BillStatus.Finalized && b.Bill.PaidAmount > 0);
        Chips[4].Count = _closed?.Count;
        Notify();
    }

    // Order number, table, bill number, invoice number or waiter; "t5" matches T05 the way the palette does.
    private static bool Matches(BillSummaryDto bill, string search)
    {
        var compact = search.Replace(" ", string.Empty, StringComparison.Ordinal).TrimStart('#');
        if (compact.Length == 0)
        {
            return true;
        }

        var table = bill.TableCode.TrimStart('T', 't').TrimStart('0');
        var wanted = compact.TrimStart('T', 't').TrimStart('0');
        return bill.OrderNumber.ToString(System.Globalization.CultureInfo.InvariantCulture).Contains(compact, StringComparison.OrdinalIgnoreCase)
            || bill.BillNumber.ToString(System.Globalization.CultureInfo.InvariantCulture).Contains(compact, StringComparison.OrdinalIgnoreCase)
            || bill.TableCode.Contains(compact, StringComparison.OrdinalIgnoreCase)
            || (wanted.Length > 0 && table.Equals(wanted, StringComparison.OrdinalIgnoreCase))
            || (bill.InvoiceNumber?.Contains(compact, StringComparison.OrdinalIgnoreCase) ?? false)
            || bill.WaiterName.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(Header));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyMessage));
    }

    private async Task RunTimerAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(Tick);
        var ticks = 0;
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                foreach (var bill in _pending)
                {
                    bill.Tick(DateTime.UtcNow);
                }

                if (++ticks % 2 == 0)
                {
                    await RefreshAsync();
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// The navigation the hosted <see cref="BillDetailViewModel"/> sees: "back to Billing" clears the selection in the
    /// workspace; everything else goes to the real navigation.
    /// </summary>
    private sealed class WorkspaceNavigation : INavigationService
    {
        private readonly CashierWorkspaceViewModel _owner;
        private readonly INavigationService _inner;

        public WorkspaceNavigation(CashierWorkspaceViewModel owner, INavigationService inner)
        {
            _owner = owner;
            _inner = inner;
        }

        public Task NavigateToAsync(string moduleKey, object? parameter = null) =>
            moduleKey == ModuleRegistry.Billing && parameter is null ? _owner.OnDetailClosedAsync() : _inner.NavigateToAsync(moduleKey, parameter);

        public Task NavigateToPageAsync<TViewModel>(object? parameter = null)
            where TViewModel : class => _inner.NavigateToPageAsync<TViewModel>(parameter);

        public Task GoHomeAsync() => _inner.GoHomeAsync();
    }
}
