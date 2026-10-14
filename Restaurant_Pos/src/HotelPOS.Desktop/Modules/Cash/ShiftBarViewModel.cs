using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Cash;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using HotelPOS.Desktop.Modules.Billing;
using HotelPOS.Desktop.Modules.Common;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Auth;
using HotelPOS.Desktop.Services.Configuration;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Realtime;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Modules.Cash;

// Lets a screen that was refused SHIFT_REQUIRED offer to open the terminal's shift.
public interface ICashDrawer
{
    Task OfferOpenShiftAsync();
}

// The bar under the top bar on billing terminals: the terminal's cash shift (or "No shift open — Open shift") and its
// actions. Open, close, handover, pay in/out and the X report open as a panel over the page.
public sealed partial class ShiftBarViewModel : ObservableObject, ICashDrawer, IDisposable
{
    private readonly ICashApi _cashApi;
    private readonly IBillingApi _billingApi;
    private readonly IRealtimeClient _realtime;
    private readonly IAuthSession _session;
    private readonly IClientSettingsService _settings;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;
    private readonly ShiftReportPrinting _printing;
    private readonly List<IDisposable> _subscriptions = new();
    private List<DenominationDto>? _denominations;

    public ShiftBarViewModel(
        ICashApi cashApi,
        IBillingApi billingApi,
        IRealtimeClient realtime,
        IAuthSession session,
        IClientSettingsService settings,
        IDialogService dialogs,
        INotificationService notifications,
        ShiftReportPrinting printing)
    {
        _cashApi = cashApi;
        _billingApi = billingApi;
        _realtime = realtime;
        _session = session;
        _settings = settings;
        _dialogs = dialogs;
        _notifications = notifications;
        _printing = printing;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasShift), nameof(HasNoShift), nameof(StatusText), nameof(ExpectedText))]
    private CashShiftDto? _shift;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPanelOpen))]
    private object? _panel;

    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDayNotice))]
    private string? _dayNotice;

    [ObservableProperty]
    private bool _isBusy;

    public bool HasShift => Shift is not null;

    public bool HasNoShift => Shift is null;

    public bool IsPanelOpen => Panel is not null;

    public bool HasDayNotice => DayNotice is not null;

    public string StatusText => Shift is { } shift
        ? $"Shift open since {shift.OpenedAtUtc.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture)} · {shift.CashierName}"
        : "No shift open — cash cannot be taken on this terminal";

    public string? ExpectedText => Shift is { } shift
        ? shift.ExpectedCash is { } expected ? $"expected {Money.Format(expected)}" : "blind count"
        : null;

    // Billing terminals of users who may operate a drawer.
    public bool AppliesHere
    {
        get
        {
            var user = _session.User;
            var operate = user is not null && (user.Roles.Contains(Roles.Admin) || user.Permissions.Contains(Permissions.ShiftOperate));
            return operate && _settings.Current.DeviceType == DeviceType.Billing;
        }
    }

    public async Task StartAsync()
    {
        Stop();
        IsVisible = AppliesHere;
        if (!IsVisible)
        {
            return;
        }

        _subscriptions.Add(_realtime.Subscribe<ShiftChangedEvent>(HubEvents.ShiftChanged, e =>
        {
            if (e.DeviceId == _settings.Current.DeviceId && Panel is null)
            {
                _ = RefreshAsync();
            }
        }));
        _subscriptions.Add(_realtime.Subscribe<BusinessDayChangedEvent>(HubEvents.BusinessDayClosed, e =>
            DayNotice = $"Business day {e.BusinessDay.ToString("dd-MM-yyyy", CultureInfo.CurrentCulture)} is closed. Bills and payments continue on the next business day."));
        _subscriptions.Add(_realtime.Subscribe<BusinessDayChangedEvent>(HubEvents.BusinessDayReopened, e =>
        {
            DayNotice = $"Business day {e.BusinessDay.ToString("dd-MM-yyyy", CultureInfo.CurrentCulture)} was reopened by {e.ChangedBy}.";
            _ = RefreshAsync();
        }));
        _realtime.Reconnected += OnReconnected;
        await RefreshAsync();
    }

    public void Stop()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
        _realtime.Reconnected -= OnReconnected;
        Panel = null;
    }

    public void Dispose() => Stop();

    public async Task RefreshAsync()
    {
        var current = await _cashApi.GetCurrentShiftAsync();
        if (current.Success)
        {
            Shift = current.Data;
        }
    }

    public async Task OfferOpenShiftAsync()
    {
        await RefreshAsync();
        if (Shift is not null)
        {
            return;
        }

        if (await _dialogs.ConfirmAsync("No cash shift open", "Cash needs an open shift on this terminal. Count the float and open a shift now?", "Open shift"))
        {
            await OpenShiftAsync();
        }
    }

    [RelayCommand]
    private void DismissDayNotice() => DayNotice = null;

    [RelayCommand]
    private void ClosePanel() => Panel = null;

    [RelayCommand]
    private async Task OpenShiftAsync()
    {
        if (await DenominationsAsync() is not { } denominations)
        {
            return;
        }

        Panel = new ShiftCountPanelViewModel(ShiftCountMode.Open, new DenominationSheetViewModel(denominations),
            $"{_settings.Current.DeviceName} · {_session.User?.DisplayName}", SubmitOpenAsync, () => Panel = null);
    }

    [RelayCommand]
    private async Task CloseShiftAsync()
    {
        if (await CountPanelAsync(ShiftCountMode.Close) is { } panel)
        {
            Panel = panel;
        }
    }

    [RelayCommand]
    private async Task HandOverAsync()
    {
        var cashiers = await _cashApi.GetCashiersAsync();
        if (!cashiers.Success || cashiers.Data is null)
        {
            _notifications.Error(ApiFailures.Describe(cashiers), cashiers.CorrelationId);
            return;
        }

        var others = cashiers.Data.Where(c => c.UserId != Shift?.CashierUserId && c.OpenShiftDevice is null).ToList();
        if (await CountPanelAsync(ShiftCountMode.HandOver, others) is { } panel)
        {
            Panel = panel;
        }
    }

    [RelayCommand]
    private void PayInOut()
    {
        if (Shift is not null)
        {
            Panel = new CashMovementPanelViewModel(SubmitMovementAsync, () => Panel = null);
        }
    }

    [RelayCommand]
    private async Task XReportAsync()
    {
        if (Shift is not { } shift)
        {
            return;
        }

        var report = await _cashApi.GetReportAsync(shift.Id);
        if (!report.Success || report.Data is null)
        {
            _notifications.Error(ApiFailures.Describe(report), report.CorrelationId);
            return;
        }

        ShowReport(report.Data);
    }

    private void ShowReport(ShiftReportDto report) =>
        Panel = new ShiftReportPanelViewModel(report, _printing, ModuleRegistry.Billing, () => Panel = null);

    private async Task<ShiftCountPanelViewModel?> CountPanelAsync(ShiftCountMode mode, IEnumerable<CashierDto>? cashiers = null)
    {
        await RefreshAsync();
        if (Shift is not { } shift)
        {
            _notifications.Warning("No shift is open on this terminal.");
            return null;
        }

        if (await DenominationsAsync() is not { } denominations)
        {
            return null;
        }

        var sheet = new DenominationSheetViewModel(denominations, shift.ExpectedCash, blind: shift.ExpectedCash is null);
        return new ShiftCountPanelViewModel(mode, sheet, $"{shift.DeviceName} · {shift.CashierName}", SubmitCountAsync, () => Panel = null, cashiers);
    }

    private async Task<List<DenominationDto>?> DenominationsAsync()
    {
        if (_denominations is null)
        {
            var result = await _cashApi.GetDenominationsAsync();
            if (!result.Success || result.Data is null)
            {
                _notifications.Error(result.IsConnectionFailure ? result.Message : ApiFailures.Describe(result), result.CorrelationId);
                return null;
            }

            _denominations = result.Data;
        }

        return _denominations;
    }

    private async Task SubmitOpenAsync(ShiftCountPanelViewModel panel)
    {
        var result = await _cashApi.OpenShiftAsync(new OpenShiftRequest { Counts = panel.Sheet.ToCounts() });
        if (!result.Success || result.Data is null)
        {
            panel.ErrorMessage = ApiFailures.Describe(result);
            if (result.HasError(ErrorCodes.ShiftAlreadyOpen))
            {
                await RefreshAsync();
            }

            return;
        }

        Shift = result.Data;
        Panel = null;
        _notifications.Success($"Shift opened with a float of {Money.Format(result.Data.FloatAmount)}.");
    }

    private async Task SubmitCountAsync(ShiftCountPanelViewModel panel)
    {
        var shift = Shift!;
        var reason = string.IsNullOrWhiteSpace(panel.Reason) ? null : panel.Reason.Trim();
        if (panel.Mode == ShiftCountMode.HandOver)
        {
            var handover = await Approvals.WithApprovalAsync(_dialogs, _billingApi, ApprovalActions.CashVariance, "the cash difference",
                approval => _cashApi.HandOverAsync(shift.Id, new HandOverShiftRequest
                {
                    Counts = panel.Sheet.ToCounts(),
                    VarianceReason = reason,
                    NextCashierUserId = panel.SelectedCashier!.UserId,
                    Approval = approval,
                    RowVersion = shift.RowVersion,
                }));
            if (CountFailed(panel, handover))
            {
                return;
            }

            Shift = handover.Data!.Next;
            ShowReport(handover.Data.Closed);
            _notifications.Success($"Shift handed over to {handover.Data.Next.CashierName}.");
            return;
        }

        var closed = await Approvals.WithApprovalAsync(_dialogs, _billingApi, ApprovalActions.CashVariance, "the cash difference",
            approval => _cashApi.CloseShiftAsync(shift.Id, new CloseShiftRequest
            {
                Counts = panel.Sheet.ToCounts(),
                VarianceReason = reason,
                Approval = approval,
                RowVersion = shift.RowVersion,
            }));
        if (CountFailed(panel, closed))
        {
            return;
        }

        Shift = null;
        ShowReport(closed.Data!);
        _notifications.Success("Shift closed.");
    }

    private bool CountFailed<T>(ShiftCountPanelViewModel panel, ApiResult<T> result)
    {
        if (result.Success && result.Data is not null)
        {
            return false;
        }

        // With a blind count the cashier learns of a difference only now: ask for the reason.
        if (result.Errors.Any(e => e.Field == "varianceReason"))
        {
            panel.ReasonRequested = true;
        }

        panel.ErrorMessage = Approvals.IsApprovalError(result) ? "Not closed: a manager must approve the difference." : ApiFailures.Describe(result);
        if (result.HasError(ErrorCodes.ConcurrencyConflict) || result.HasError(ErrorCodes.InvalidStateTransition))
        {
            _ = RefreshAsync();
        }

        return true;
    }

    private async Task SubmitMovementAsync(CashMovementPanelViewModel panel)
    {
        var shift = Shift!;
        var result = await Approvals.WithApprovalAsync(_dialogs, _billingApi, ApprovalActions.PayOut, "this cash out of the drawer",
            approval => _cashApi.AddMovementAsync(shift.Id, new AddCashMovementRequest
            {
                Type = panel.SelectedType.Type,
                Amount = panel.Amount,
                Reason = panel.Reason.Trim(),
                Approval = approval,
            }));
        if (!result.Success || result.Data is null)
        {
            panel.ErrorMessage = Approvals.IsApprovalError(result) ? "Not recorded: a manager must approve it." : ApiFailures.Describe(result);
            return;
        }

        Shift = result.Data.Shift;
        Panel = null;
        _notifications.Success($"{panel.SelectedType.Title} of {Money.Format(panel.Amount)} recorded.");
    }

    private void OnReconnected(object? sender, EventArgs e) => _ = RefreshAsync();
}
