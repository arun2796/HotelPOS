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
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Realtime;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Modules.Cash;

public sealed record ShiftRow(CashShiftDto Shift)
{
    public string Opened => Shift.OpenedAtUtc.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture);

    public string Closed => Shift.ClosedAtUtc?.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture) ?? "—";

    public string FloatText => Money.Format(Shift.FloatAmount);

    public string ExpectedText => Shift.ExpectedCash is { } e ? Money.Format(e) : "—";

    public string CountedText => Shift.CountedCash is { } c ? Money.Format(c) : "—";

    public string VarianceText => Shift.Variance is { } v ? (v == 0 ? "0.00" : (v < 0 ? "−" : "+") + Money.Format(Math.Abs(v))) : "—";

    public string StatusText => Shift.Status switch
    {
        CashShiftStatus.HandedOver => "Handed over",
        CashShiftStatus.ForceClosed => "Force closed",
        _ => Shift.Status.ToString(),
    };

    public bool IsOpen => Shift.Status == CashShiftStatus.Open;

    /// <summary>A counted shift whose cash did not match: the grid colours the variance.</summary>
    public bool HasVariance => Shift.Variance is { } v && v != 0;
}

// Managers: every shift of a business day with its expected, counted and variance; X/Z reports, force close, reopen.
public sealed partial class ShiftsViewModel : ViewModelBase, INavigationAware, IRefreshable, IDisposable
{
    private readonly ICashApi _cashApi;
    private readonly IBillingApi _billingApi;
    private readonly IRealtimeClient _realtime;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;
    private readonly ShiftReportPrinting _printing;
    private IDisposable? _subscription;

    public ShiftsViewModel(
        ICashApi cashApi,
        IBillingApi billingApi,
        IRealtimeClient realtime,
        IDialogService dialogs,
        INotificationService notifications,
        ShiftReportPrinting printing)
    {
        _cashApi = cashApi;
        _billingApi = billingApi;
        _realtime = realtime;
        _dialogs = dialogs;
        _notifications = notifications;
        _printing = printing;
    }

    public ObservableCollection<ShiftRow> Shifts { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DayText))]
    private DateOnly? _day;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(CanForceClose), nameof(CanReopen))]
    private ShiftRow? _selectedShift;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPanelOpen))]
    private object? _panel;

    public bool IsPanelOpen => Panel is not null;

    public bool HasSelection => SelectedShift is not null;

    public bool CanForceClose => SelectedShift?.IsOpen == true;

    public bool CanReopen => SelectedShift?.Shift.Status is CashShiftStatus.Closed or CashShiftStatus.ForceClosed;

    public bool IsEmpty => Shifts.Count == 0 && ErrorMessage is null;

    public string DayText => Day is { } d ? d.ToString("dddd, dd MMM yyyy", CultureInfo.CurrentCulture) : "Today";

    public async Task OnNavigatedToAsync(object? parameter)
    {
        _subscription = _realtime.Subscribe<ShiftChangedEvent>(HubEvents.ShiftChanged, changed =>
        {
            if (Panel is null)
            {
                _ = RefreshAsync();
            }
        });
        await RefreshAsync();
    }

    public void OnNavigatedFrom() => Dispose();

    public void Dispose()
    {
        _subscription?.Dispose();
        _subscription = null;
    }

    public async Task RefreshAsync()
    {
        var selected = SelectedShift?.Shift.Id;
        var shifts = await FetchAsync(_cashApi.GetShiftsAsync(Day));
        if (shifts is null)
        {
            OnPropertyChanged(nameof(IsEmpty));
            return;
        }

        ErrorMessage = null;
        Shifts.Clear();
        foreach (var shift in shifts)
        {
            Shifts.Add(new ShiftRow(shift));
        }

        SelectedShift = Shifts.FirstOrDefault(s => s.Shift.Id == selected);
        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private Task PreviousDayAsync()
    {
        Day = (Day ?? Shifts.FirstOrDefault()?.Shift.BusinessDay ?? DateOnly.FromDateTime(DateTime.Now)).AddDays(-1);
        return RefreshAsync();
    }

    [RelayCommand]
    private Task NextDayAsync()
    {
        Day = (Day ?? DateOnly.FromDateTime(DateTime.Now)).AddDays(1);
        return RefreshAsync();
    }

    [RelayCommand]
    private Task TodayAsync()
    {
        Day = null;
        return RefreshAsync();
    }

    [RelayCommand]
    private async Task ShowReportAsync()
    {
        if (SelectedShift is not { } row)
        {
            return;
        }

        var report = await FetchAsync(_cashApi.GetReportAsync(row.Shift.Id));
        if (report is not null)
        {
            Panel = new ShiftReportPanelViewModel(report, _printing, ModuleRegistry.Shifts, () => Panel = null);
        }
    }

    [RelayCommand]
    private async Task ForceCloseAsync()
    {
        if (SelectedShift is not { IsOpen: true } row)
        {
            return;
        }

        var denominations = await FetchAsync(_cashApi.GetDenominationsAsync());
        if (denominations is null)
        {
            return;
        }

        Panel = new ShiftCountPanelViewModel(ShiftCountMode.ForceClose, new DenominationSheetViewModel(denominations, row.Shift.ExpectedCash),
            $"{row.Shift.DeviceName} · {row.Shift.CashierName}", panel => SubmitForceCloseAsync(row.Shift, panel), () => Panel = null);
    }

    [RelayCommand]
    private async Task ReopenAsync()
    {
        if (SelectedShift is not { } row || !CanReopen)
        {
            return;
        }

        var reason = await _dialogs.PromptAsync("Reopen shift", $"Reopen the shift of {row.Shift.CashierName} on {row.Shift.DeviceName}? It is counted again when it closes. Reason:", "Reopen");
        if (string.IsNullOrWhiteSpace(reason))
        {
            return;
        }

        await ExecuteAsync(async () =>
        {
            var result = await Approvals.WithApprovalAsync(_dialogs, _billingApi, ApprovalActions.ReopenShift, "reopening the shift",
                approval => _cashApi.ReopenShiftAsync(row.Shift.Id, new ReopenShiftRequest { Reason = reason.Trim(), Approval = approval, RowVersion = row.Shift.RowVersion }));
            if (!result.Success)
            {
                ErrorMessage = ApiFailures.Describe(result);
                return;
            }

            _notifications.Success("Shift reopened.");
            await RefreshAsync();
        });
    }

    private async Task SubmitForceCloseAsync(CashShiftDto shift, ShiftCountPanelViewModel panel)
    {
        var result = await _cashApi.ForceCloseAsync(shift.Id, new ForceCloseShiftRequest
        {
            Counts = panel.Sheet.ToCounts(),
            Reason = panel.Reason.Trim(),
            RowVersion = shift.RowVersion,
        });
        if (!result.Success || result.Data is null)
        {
            panel.ErrorMessage = ApiFailures.Describe(result);
            return;
        }

        Panel = new ShiftReportPanelViewModel(result.Data, _printing, ModuleRegistry.Shifts, () => Panel = null);
        _notifications.Success("Shift force-closed.");
        await RefreshAsync();
    }
}
