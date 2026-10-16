using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Cash;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Desktop.Modules.Billing;
using HotelPOS.Desktop.Modules.Common;
using HotelPOS.Desktop.Modules.Dashboard;
using HotelPOS.Desktop.Modules.Printing;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Realtime;
using HotelPOS.Desktop.Services.Printing;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Modules.Cash;

// Home of the Cashier role (Phase 20.6): pending bills, this terminal's open shift (cash, sales, expected cash) and the
// state of the business day, each tile opening the screen behind it. Only existing endpoints: the billing queue, the
// current shift with its X report, and the business-day events. The Day End precheck is for managers, so a cashier's
// tile follows the day-closed/reopened events instead. The Print failures tile (20.8) counts this terminal's failed prints from the print queue and opens the print log on its Failed filter.
public sealed partial class CashierHomeViewModel : ViewModelBase, INavigationAware, IRefreshable, IDisposable
{
    private static readonly string[] RefreshEvents =
    {
        HubEvents.BillRequested, HubEvents.BillUpdated, HubEvents.PaymentCompleted, HubEvents.ShiftChanged,
    };

    private readonly IBillingApi _billingApi;
    private readonly ICashApi _cashApi;
    private readonly IRealtimeClient _realtime;
    private readonly INavigationService _navigation;
    private readonly IPrintQueue _printQueue;
    private readonly IUiDispatcher _ui;
    private readonly List<IDisposable> _subscriptions = new();
    private CancellationTokenSource? _debounce;
    private string _dayStatus = "Open";
    private string _dayDetail = "Bills and payments continue as normal";

    public CashierHomeViewModel(IBillingApi billingApi, ICashApi cashApi, IRealtimeClient realtime, INavigationService navigation, IPrintQueue printQueue, IUiDispatcher ui)
    {
        _billingApi = billingApi;
        _cashApi = cashApi;
        _realtime = realtime;
        _navigation = navigation;
        _printQueue = printQueue;
        _ui = ui;
    }

    public ObservableCollection<KpiTile> Tiles { get; } = new();

    public ObservableCollection<BillQueueItemViewModel> WaitingBills { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWaitingBills))]
    private int _pendingCount;

    [ObservableProperty]
    private string _updatedText = string.Empty;

    public bool HasWaitingBills => WaitingBills.Count > 0;

    public int RefreshCount { get; private set; }

    // Events arrive in bursts; the home reloads once, a moment later.
    internal TimeSpan DebounceDelay { get; set; } = TimeSpan.FromSeconds(2);

    public async Task OnNavigatedToAsync(object? parameter)
    {
        _printQueue.Changed -= OnPrintQueueChanged;
        _printQueue.Changed += OnPrintQueueChanged;
        foreach (var eventName in RefreshEvents)
        {
            _subscriptions.Add(_realtime.Subscribe<object>(eventName, notice => ScheduleRefresh()));
        }

        _subscriptions.Add(_realtime.Subscribe<BusinessDayChangedEvent>(HubEvents.BusinessDayClosed, e =>
        {
            SetDay("Closed", $"{e.BusinessDay.ToString("dd-MM-yyyy", CultureInfo.CurrentCulture)} was closed; bills continue on the next day");
        }));
        _subscriptions.Add(_realtime.Subscribe<BusinessDayChangedEvent>(HubEvents.BusinessDayReopened, e =>
        {
            SetDay("Reopened", $"{e.BusinessDay.ToString("dd-MM-yyyy", CultureInfo.CurrentCulture)} reopened by {e.ChangedBy}");
        }));

        await RefreshAsync();
    }

    public void OnNavigatedFrom() => Dispose();

    public void Dispose()
    {
        _printQueue.Changed -= OnPrintQueueChanged;
        DisposeSubscriptions();
    }

    private void DisposeSubscriptions()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
        _debounce?.Cancel();
        _debounce?.Dispose();
        _debounce = null;
    }

    public async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var pending = await _billingApi.GetPendingAsync();
            var shift = await _cashApi.GetCurrentShiftAsync();
            ShiftReportDto? report = null;
            if (shift is { Success: true, Data: { } open })
            {
                var x = await _cashApi.GetReportAsync(open.Id);
                report = x is { Success: true } ? x.Data : null;
            }

            if (pending is not { Success: true, Data: not null })
            {
                ErrorMessage = pending is { IsConnectionFailure: true } ? "Connection unavailable. Figures will refresh when the server is back." : pending?.Message;
                return;
            }

            ErrorMessage = null;
            RefreshCount++;
            UpdatedText = "Updated " + DateTime.Now.ToString("HH:mm:ss", CultureInfo.CurrentCulture);
            Build(pending.Data, shift is { Success: true } ? shift.Data : null, report);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void ScheduleRefresh()
    {
        _debounce?.Cancel();
        _debounce?.Dispose();
        var source = new CancellationTokenSource();
        _debounce = source;
        _ = DelayedRefreshAsync(source.Token);
    }

    [RelayCommand]
    private Task RefreshNowAsync() => RefreshAsync();

    [RelayCommand]
    private Task OpenAsync(KpiTile? tile) => tile?.Module is { } module
        ? _navigation.NavigateToAsync(module, module == ModuleRegistry.PrintLog ? new PrintLogContext(PrintLogFilter.Failed) : null)
        : Task.CompletedTask;

    private async Task DelayedRefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(DebounceDelay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await RefreshAsync();
    }

    private void SetDay(string status, string detail)
    {
        _dayStatus = status;
        _dayDetail = detail;
        var existing = Tiles.FirstOrDefault(t => t.Title == DayTitle);
        if (existing is not null)
        {
            Tiles[Tiles.IndexOf(existing)] = DayTile();
        }
    }

    private const string DayTitle = "Business day";

    // Failed prints of this terminal, from the print queue (the server has no cross-document print list). The tile opens
    // the print log on its Failed filter.
    private KpiTile PrintTile()
    {
        var failed = _printQueue.FailedCount;
        return new KpiTile(PrintTitle, failed.ToString(CultureInfo.CurrentCulture),
            failed == 0 ? "everything printed" : "tap to retry them in the print log", "", ModuleRegistry.PrintLog);
    }

    private const string PrintTitle = "Print failures";

    private void OnPrintQueueChanged(object? sender, EventArgs e) => _ui.Post(() =>
    {
        var existing = Tiles.FirstOrDefault(t => t.Title == PrintTitle);
        if (existing is not null)
        {
            Tiles[Tiles.IndexOf(existing)] = PrintTile();
        }
    });

    private KpiTile DayTile() => new(DayTitle, _dayStatus, _dayDetail, "\uE930");

    private void Build(List<BillSummaryDto> pending, CashShiftDto? shift, ShiftReportDto? report)
    {
        var now = DateTime.UtcNow;
        var ordered = pending.OrderBy(b => b.RequestedAtUtc).ToList();
        PendingCount = ordered.Count;
        var oldest = ordered.Count == 0 ? string.Empty : $"oldest waiting {Math.Max(0, (int)(now - ordered[0].RequestedAtUtc).TotalMinutes)} min";

        WaitingBills.Clear();
        foreach (var bill in ordered.Take(6))
        {
            var item = new BillQueueItemViewModel(bill, string.Empty);
            item.Tick(now);
            WaitingBills.Add(item);
        }

        OnPropertyChanged(nameof(HasWaitingBills));

        var tiles = new List<KpiTile>
        {
            new("Pending bills", ordered.Count.ToString(CultureInfo.CurrentCulture), ordered.Count == 0 ? "nothing waiting" : oldest, "\uE8C7", ModuleRegistry.Billing),
        };

        if (shift is null)
        {
            tiles.Add(new KpiTile("My shift", "No shift", "open one from the shift bar at the top before taking cash", "\uE823"));
            tiles.Add(new KpiTile("Expected cash", "—", "no open shift on this terminal", "\uE8D4"));
        }
        else
        {
            var sales = report is null ? null : (decimal?)report.Payments.Sum(p => p.Net);
            tiles.Add(new KpiTile("My shift",
                sales is { } total ? Money.Format(total) : "Open",
                $"takings since {shift.OpenedAtUtc.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture)} · float {Money.Format(shift.FloatAmount)}", "\uE823"));
            var expected = report is { CashHidden: false, ExpectedCash: { } e } ? e : shift.ExpectedCash;
            tiles.Add(new KpiTile("Expected cash",
                expected is { } cash ? Money.Format(cash) : "Blind count",
                report is null ? "in the drawer" : $"in {Money.Format(report.PayIns)} · out {Money.Format(report.PayOuts)} · cash sales {Money.Format(report.CashSales)}", "\uE8D4"));
        }

        tiles.Add(PrintTile());
        tiles.Add(DayTile());
        Tiles.Clear();
        foreach (var tile in tiles)
        {
            Tiles.Add(tile);
        }
    }
}
