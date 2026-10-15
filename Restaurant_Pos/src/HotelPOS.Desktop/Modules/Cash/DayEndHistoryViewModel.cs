using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Cash;
using HotelPOS.Contracts.Enums;
using HotelPOS.Desktop.Modules.Billing;
using HotelPOS.Desktop.Modules.Common;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Modules.Cash;

public sealed record DayEndRow(BusinessDayCloseDto Close)
{
    public string DayText => Close.BusinessDay.ToString("ddd dd MMM yyyy", CultureInfo.CurrentCulture);

    public string StatusText => Close.Status == BusinessDayStatus.Closed ? "Closed" : "Reopened";

    public string ClosedText => $"{Close.ClosedAtUtc.ToLocalTime().ToString("dd MMM HH:mm", CultureInfo.CurrentCulture)} · {Close.ClosedByName}";

    public string NetSalesText => Money.Format(Close.Summary.NetSales);

    public string BillsText => Close.Summary.Bills.ToString(CultureInfo.CurrentCulture);

    public string VarianceText => Money.Format(Close.Summary.CashVariance);
}

// Every Day End run: totals, overrides, the backup and the pack; a manager reopens a closed day here.
public sealed partial class DayEndHistoryViewModel : ViewModelBase, INavigationAware, IRefreshable
{
    private readonly ICashApi _cashApi;
    private readonly IBillingApi _billingApi;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;

    public DayEndHistoryViewModel(ICashApi cashApi, IBillingApi billingApi, IDialogService dialogs, INotificationService notifications)
    {
        _cashApi = cashApi;
        _billingApi = billingApi;
        _dialogs = dialogs;
        _notifications = notifications;
    }

    public ObservableCollection<DayEndRow> Days { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(CanReopen), nameof(Details), nameof(Overrides))]
    private DayEndRow? _selectedDay;

    public bool HasSelection => SelectedDay is not null;

    public bool CanReopen => SelectedDay?.Close.Status == BusinessDayStatus.Closed;

    public bool IsEmpty => Days.Count == 0 && ErrorMessage is null;

    public IReadOnlyList<ReportLine> Details => SelectedDay?.Close is { } c
        ? new[]
        {
            new ReportLine("Bills", c.Summary.Bills.ToString(CultureInfo.CurrentCulture)),
            new ReportLine("Net sales", Money.Format(c.Summary.NetSales), IsTotal: true),
            new ReportLine("Non-till collections", Money.Format(c.Summary.NonTillTotal)),
            new ReportLine("Cash variance", Money.Format(c.Summary.CashVariance)),
            new ReportLine("Backup", c.BackupFile ?? "—"),
            new ReportLine("Report pack", c.PackFolder ?? "not written"),
        }.Concat(c.ReopenReason is null
            ? Array.Empty<ReportLine>()
            : new[] { new ReportLine($"Reopened by {c.ReopenedByName}", c.ReopenReason) }).ToList()
        : Array.Empty<ReportLine>();

    public IReadOnlyList<ReportLine> Overrides => SelectedDay?.Close.Overrides.Select(o => new ReportLine($"{o.Count} × {o.Code}", o.Reason)).ToList()
        ?? new List<ReportLine>();

    public Task OnNavigatedToAsync(object? parameter) => RefreshAsync();

    public void OnNavigatedFrom()
    {
    }

    public async Task RefreshAsync()
    {
        var selected = SelectedDay?.Close.BusinessDay;
        var days = await FetchAsync(_cashApi.GetDayEndHistoryAsync());
        if (days is not null)
        {
            ErrorMessage = null;
            Days.Clear();
            foreach (var day in days)
            {
                Days.Add(new DayEndRow(day));
            }

            SelectedDay = Days.FirstOrDefault(d => d.Close.BusinessDay == selected) ?? Days.FirstOrDefault();
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private async Task ReopenAsync()
    {
        if (SelectedDay is not { } row || !CanReopen)
        {
            return;
        }

        var reason = await _dialogs.PromptAsync("Reopen business day",
            $"Reopen {row.DayText}? Bills and payments of the day can change again until Day End runs again. Reason:", "Reopen");
        if (string.IsNullOrWhiteSpace(reason))
        {
            return;
        }

        await ExecuteAsync(async () =>
        {
            var result = await Approvals.WithApprovalAsync(_dialogs, _billingApi, ApprovalActions.ReopenDay, "reopening the day",
                approval => _cashApi.ReopenDayAsync(row.Close.BusinessDay, new ReopenDayRequest { Reason = reason.Trim(), Approval = approval }));
            if (!result.Success)
            {
                ErrorMessage = ApiFailures.Describe(result);
                return;
            }

            _notifications.Success($"{row.DayText} reopened.");
            await RefreshAsync();
        });
    }
}
