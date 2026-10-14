using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Gst;
using HotelPOS.Desktop.Modules.Common;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Modules.Gst;

public sealed record PeriodRow(PeriodDto Period)
{
    public string Label => Period.Label;

    public bool IsYear => Period.Kind == PeriodKind.Year;

    public string StatusText => Period.Status switch
    {
        PeriodStatus.Closed => "Closed",
        PeriodStatus.Reopened => "Reopened",
        _ => "Open",
    };

    public string ClosedText => Period.ClosedAtUtc is { } at
        ? $"{at.ToLocalTime().ToString("dd MMM yyyy HH:mm", CultureInfo.CurrentCulture)} · {Period.ClosedBy}"
        : string.Empty;

    public string PackText => Period.PackFolder ?? string.Empty;
}

public sealed record PeriodCheckRow(string Title, string Mark, string Details);

// Month and year close (administrators): the financial year's months and the year with their status, the pre-check of
// the selected one, and close / reopen. A closed period accepts no new, edited, voided or refunded bills or payments.
public sealed partial class PeriodCloseViewModel : ViewModelBase, INavigationAware, IRefreshable
{
    private readonly IGstApi _api;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;

    public PeriodCloseViewModel(IGstApi api, IDialogService dialogs, INotificationService notifications)
    {
        _api = api;
        _dialogs = dialogs;
        _notifications = notifications;
        var today = DateTime.Today;
        _financialYear = today.Month >= 4 ? today.Year : today.Year - 1;
    }

    public ObservableCollection<PeriodRow> Periods { get; } = new();

    public ObservableCollection<PeriodCheckRow> Checks { get; } = new();

    public IReadOnlyList<int> FinancialYears { get; } = Enumerable.Range(DateTime.Today.Year - 5, 7).Reverse().ToList();

    [ObservableProperty]
    private int _financialYear;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(CanClose), nameof(CanReopen), nameof(SelectedTitle))]
    private PeriodRow? _selectedPeriod;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanClose))]
    private bool _precheckPassed;

    public bool HasSelection => SelectedPeriod is not null;

    public string SelectedTitle => SelectedPeriod?.Label ?? string.Empty;

    public bool CanClose => SelectedPeriod is { Period.Status: not PeriodStatus.Closed } && PrecheckPassed;

    public bool CanReopen => SelectedPeriod is { Period.Status: PeriodStatus.Closed };

    public Task OnNavigatedToAsync(object? parameter) => RefreshAsync();

    public void OnNavigatedFrom()
    {
    }

    partial void OnFinancialYearChanged(int value) => _ = RefreshAsync();

    partial void OnSelectedPeriodChanged(PeriodRow? value) => _ = LoadChecksAsync();

    public async Task RefreshAsync()
    {
        var selected = SelectedPeriod?.Period;
        var periods = await FetchAsync(_api.GetPeriodsAsync(FinancialYear));
        if (periods is null)
        {
            return;
        }

        ErrorMessage = null;
        Periods.Clear();
        foreach (var period in periods)
        {
            Periods.Add(new PeriodRow(period));
        }

        SelectedPeriod = Periods.FirstOrDefault(p => selected is not null && p.Period.Kind == selected.Kind && p.Period.Period == selected.Period)
            ?? Periods.FirstOrDefault(p => p.Period.Status != PeriodStatus.Closed && p.Period.Kind == PeriodKind.Month) ?? Periods.FirstOrDefault();
    }

    private async Task LoadChecksAsync()
    {
        Checks.Clear();
        PrecheckPassed = false;
        if (SelectedPeriod is not { } row)
        {
            return;
        }

        if (row.Period.Status == PeriodStatus.Closed)
        {
            return;
        }

        var precheck = await FetchAsync(_api.GetPrecheckAsync(row.Period.Kind, row.Period.Period));
        if (precheck is null || SelectedPeriod != row)
        {
            return;
        }

        foreach (var check in precheck.Checks)
        {
            Checks.Add(new PeriodCheckRow(check.Title, check.Passed ? "" : "", string.Join(Environment.NewLine, check.Details)));
        }

        PrecheckPassed = precheck.CanClose;
    }

    [RelayCommand]
    private async Task CloseAsync()
    {
        if (SelectedPeriod is not { } row || !CanClose)
        {
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync($"Close {row.Label}",
            $"Close {row.Label}? No new, edited, voided or refunded bills or payments dated in it will be accepted, and the GST pack (GSTR-1 JSON, Excel, PDF) is written next to the backups. An administrator can reopen it with a reason.",
            "Close period", destructive: true);
        if (!confirmed)
        {
            return;
        }

        await ExecuteAsync(async () =>
        {
            var result = await _api.CloseAsync(new ClosePeriodRequest { Kind = row.Period.Kind, Period = row.Period.Period });
            if (!result.Success || result.Data is null)
            {
                ErrorMessage = ApiFailures.Describe(result);
                return;
            }

            _notifications.Success($"{row.Label} closed." + (result.Data.Period.PackFolder is { } folder ? $" Pack: {folder}" : string.Empty));
            foreach (var warning in result.Data.Warnings)
            {
                _notifications.Error(warning);
            }

            await RefreshAsync();
        });
    }

    [RelayCommand]
    private async Task ReopenAsync()
    {
        if (SelectedPeriod is not { } row || !CanReopen)
        {
            return;
        }

        var reason = await _dialogs.PromptAsync($"Reopen {row.Label}",
            $"Reopen {row.Label}? Bills and payments dated in it can change again until it is closed again. The reason is recorded in the audit log:", "Reopen");
        if (string.IsNullOrWhiteSpace(reason))
        {
            return;
        }

        await ExecuteAsync(async () =>
        {
            var result = await _api.ReopenAsync(new ReopenPeriodRequest { Kind = row.Period.Kind, Period = row.Period.Period, Reason = reason.Trim() });
            if (!result.Success)
            {
                ErrorMessage = ApiFailures.Describe(result);
                return;
            }

            _notifications.Success($"{row.Label} reopened.");
            await RefreshAsync();
        });
    }
}
