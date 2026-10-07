using System.Collections.ObjectModel;
using System.Globalization;
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

namespace HotelPOS.Desktop.Modules.Billing;

public sealed partial class BillQueueItemViewModel : ObservableObject
{
    public BillQueueItemViewModel(BillSummaryDto bill, string thisDevice)
    {
        Bill = bill;
        IsMine = bill.ClaimedByDevice is { } device && string.Equals(device, thisDevice, StringComparison.OrdinalIgnoreCase);
    }

    public BillSummaryDto Bill { get; }

    public int Id => Bill.Id;

    public bool IsMine { get; }

    public string TableCode => Bill.TableCode;

    public string OrderText => $"Order #{Bill.OrderNumber} · {Bill.WaiterName}";

    public string TotalText => Money.Format(Bill.GrandTotal);

    public string StatusText => Bill.Status == BillStatus.Finalized
        ? Bill.PaidAmount > 0 ? $"Part paid · {Money.Format(Bill.GrandTotal - Bill.PaidAmount)} due" : $"Invoice {Bill.InvoiceNumber}"
        : "Waiting";

    public string? ClaimBadge => Bill.ClaimedByDevice ?? Bill.ClaimedByUser;

    public bool IsClaimed => ClaimBadge is not null;

    public bool IsClaimedByOther => IsClaimed && !IsMine;

    [ObservableProperty]
    private string _waitingText = string.Empty;

    public void Tick(DateTime nowUtc)
    {
        var minutes = Math.Max(0, (int)(nowUtc - Bill.RequestedAtUtc).TotalMinutes);
        WaitingText = minutes < 1 ? "just now" : string.Create(CultureInfo.CurrentCulture, $"{minutes} min");
    }
}

public sealed partial class BillingQueueViewModel : ObservableObject, INavigationAware, IRefreshable, IDisposable
{
    private readonly IBillingApi _billingApi;
    private readonly IRealtimeClient _realtime;
    private readonly INavigationService _navigation;
    private readonly IClientSettingsService _settings;
    private readonly INotificationService _notifications;
    private readonly List<IDisposable> _subscriptions = new();
    private CancellationTokenSource? _timer;

    public BillingQueueViewModel(
        IBillingApi billingApi,
        IRealtimeClient realtime,
        INavigationService navigation,
        IClientSettingsService settings,
        INotificationService notifications)
    {
        _billingApi = billingApi;
        _realtime = realtime;
        _navigation = navigation;
        _settings = settings;
        _notifications = notifications;
    }

    public ObservableCollection<BillQueueItemViewModel> Bills { get; } = new();

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isBusy;

    public bool IsEmpty => Bills.Count == 0 && ErrorMessage is null;

    public string Header => Bills.Count == 1 ? "1 bill waiting" : $"{Bills.Count} bills waiting";

    public async Task OnNavigatedToAsync(object? parameter)
    {
        _subscriptions.Add(_realtime.Subscribe<BillRequestedEvent>(HubEvents.BillRequested, notice => _ = RefreshAsync()));
        _subscriptions.Add(_realtime.Subscribe<BillUpdatedEvent>(HubEvents.BillUpdated, notice => _ = RefreshAsync()));
        _subscriptions.Add(_realtime.Subscribe<PaymentCompletedEvent>(HubEvents.PaymentCompleted, e => Remove(e.BillId)));
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
        Bills.Clear();
        foreach (var bill in result.Data.OrderBy(b => b.RequestedAtUtc))
        {
            var item = new BillQueueItemViewModel(bill, _settings.Current.DeviceName);
            item.Tick(now);
            Bills.Add(item);
        }

        Notify();
    }

    public void Remove(int billId)
    {
        if (Bills.FirstOrDefault(b => b.Id == billId) is { } item)
        {
            Bills.Remove(item);
            Notify();
        }
    }

    [RelayCommand]
    private Task RefreshListAsync() => RefreshAsync();

    [RelayCommand]
    private async Task OpenAsync(BillQueueItemViewModel? item)
    {
        if (item is null || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var claim = await _billingApi.ClaimAsync(item.Id);
            if (claim.Success || claim.HasError(ErrorCodes.BillClaimed))
            {
                // A bill held elsewhere still opens, read-only, so the cashier can see who has it.
                await _navigation.NavigateToPageAsync<BillDetailViewModel>(item.Id);
                return;
            }

            _notifications.Error(ApiFailures.Describe(claim), claim.CorrelationId);
            await RefreshAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(Header));
    }

    private async Task RunTimerAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        var ticks = 0;
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                foreach (var bill in Bills)
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
}
