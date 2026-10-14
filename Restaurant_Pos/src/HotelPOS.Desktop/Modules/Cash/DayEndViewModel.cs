using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Cash;
using HotelPOS.Contracts.Enums;
using HotelPOS.Desktop.Modules.Billing;
using HotelPOS.Desktop.Modules.Common;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Modules.Cash;

public enum DayEndStep
{
    Checks = 1,
    Summary = 2,
    Running = 3,
    Done = 4,
}

public sealed partial class DayEndCheckItemViewModel : ObservableObject
{
    public DayEndCheckItemViewModel(DayEndCheckItemDto item)
    {
        Item = item;
    }

    public DayEndCheckItemDto Item { get; }

    public string Title => $"{Item.Count} {Item.Title.ToLowerInvariant()}";

    public string Details => string.Join("\n", Item.Details) + (Item.Count > Item.Details.Count ? $"\n… and {Item.Count - Item.Details.Count} more" : string.Empty);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverridden))]
    private string _overrideReason = string.Empty;

    [ObservableProperty]
    private bool _isEditingOverride;

    public bool IsOverridden => !string.IsNullOrWhiteSpace(OverrideReason);

    // The screen where the item can be settled instead of overridden.
    public string Module => Item.Code switch
    {
        DayEndCheckCodes.OpenOrders => ModuleRegistry.Tables,
        DayEndCheckCodes.UnpaidBills => ModuleRegistry.Billing,
        DayEndCheckCodes.OpenShifts => ModuleRegistry.Shifts,
        _ => ModuleRegistry.KitchenDisplay,
    };
}

// Day End for managers: 1 checks (settle or override each blocking item), 2 the day's totals, 3 backup → lock → pack,
// 4 done. The server does the work and refuses anything left unexplained.
public sealed partial class DayEndViewModel : ViewModelBase, INavigationAware
{
    private readonly ICashApi _cashApi;
    private readonly INavigationService _navigation;
    private readonly INotificationService _notifications;

    public DayEndViewModel(ICashApi cashApi, INavigationService navigation, INotificationService notifications)
    {
        _cashApi = cashApi;
        _navigation = navigation;
        _notifications = notifications;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChecks), nameof(IsSummary), nameof(IsRunning), nameof(IsDone), nameof(StepText))]
    private DayEndStep _step = DayEndStep.Checks;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DayText), nameof(IsAlreadyClosed), nameof(Totals), nameof(Payments), nameof(Collections), nameof(Shifts), nameof(HasNoBlockingItems))]
    private DayEndPrecheckDto? _precheck;

    [ObservableProperty]
    private DayEndResultDto? _result;

    [ObservableProperty]
    private string _collectionMethod = "Owner UPI";

    [ObservableProperty]
    private string _collectionAmount = string.Empty;

    [ObservableProperty]
    private string _collectionReference = string.Empty;

    public ObservableCollection<DayEndCheckItemViewModel> Items { get; } = new();

    public bool IsChecks => Step == DayEndStep.Checks;

    public bool IsSummary => Step == DayEndStep.Summary;

    public bool IsRunning => Step == DayEndStep.Running;

    public bool IsDone => Step == DayEndStep.Done;

    public string StepText => $"Step {(int)Step} of 4";

    public DateOnly? Day => Precheck?.BusinessDay;

    public string DayText => Precheck is { } p ? "Day End · " + p.BusinessDay.ToString("dddd, dd MMM yyyy", CultureInfo.CurrentCulture) : "Day End";

    public bool IsAlreadyClosed => Precheck?.Status == BusinessDayStatus.Closed;

    public bool HasNoBlockingItems => Items.Count == 0;

    public bool CanContinue => Precheck is not null && !IsAlreadyClosed && Items.All(i => i.IsOverridden);

    public IReadOnlyList<ReportLine> Totals => Precheck?.Summary is { } s
        ? new[]
        {
            new ReportLine("Bills settled", s.Bills.ToString(CultureInfo.CurrentCulture)),
            new ReportLine("Gross sales", Money.Format(s.GrossSales)),
            new ReportLine("Discounts", Money.Format(s.Discounts)),
            new ReportLine("Tax", Money.Format(s.Tax)),
            new ReportLine("Refunds", Money.Format(s.Refunds)),
            new ReportLine("Net sales", Money.Format(s.NetSales), IsTotal: true),
            new ReportLine("Non-till collections", Money.Format(s.NonTillTotal)),
            new ReportLine("Cash variance (closed shifts)", Money.Format(s.CashVariance)),
        }
        : Array.Empty<ReportLine>();

    public IReadOnlyList<ReportLine> Payments => Precheck?.Summary.Payments.Select(p => new ReportLine($"{p.Method} · {p.Payments}", Money.Format(p.Net))).ToList()
        ?? new List<ReportLine>();

    public IReadOnlyList<ReportLine> Collections => Precheck?.Summary.NonTillCollections
        .Select(c => new ReportLine($"{c.Method}{(c.Reference is null ? string.Empty : " · " + c.Reference)}", Money.Format(c.Amount))).ToList()
        ?? new List<ReportLine>();

    public IReadOnlyList<ReportLine> Shifts => Precheck?.Summary.Shifts.Select(s => new ReportLine(
        $"{s.DeviceName} · {s.CashierName} · {s.Status}",
        s.Variance is { } v ? (v == 0 ? "no difference" : (v < 0 ? "short " : "over ") + Money.Format(Math.Abs(v))) : "open")).ToList()
        ?? new List<ReportLine>();

    public string ResultFiles => Result is { } r ? string.Join("\n", r.PackFiles) : string.Empty;

    public Task OnNavigatedToAsync(object? parameter) => LoadAsync(parameter as DateOnly?);

    public void OnNavigatedFrom()
    {
    }

    public async Task LoadAsync(DateOnly? day = null)
    {
        var precheck = await FetchAsync(_cashApi.GetPrecheckAsync(day));
        if (precheck is null)
        {
            return;
        }

        Items.Clear();
        foreach (var item in precheck.BlockingItems)
        {
            var vm = new DayEndCheckItemViewModel(item);
            vm.PropertyChanged += (_, _) => OnPropertyChanged(nameof(CanContinue));
            Items.Add(vm);
        }

        Precheck = precheck;
        OnPropertyChanged(nameof(CanContinue));
        OnPropertyChanged(nameof(HasNoBlockingItems));
    }

    [RelayCommand]
    private Task RecheckAsync() => LoadAsync(Day);

    [RelayCommand]
    private Task OpenItemAsync(DayEndCheckItemViewModel? item) =>
        item is null ? Task.CompletedTask : _navigation.NavigateToAsync(item.Module);

    [RelayCommand]
    private void EditOverride(DayEndCheckItemViewModel? item)
    {
        if (item is not null)
        {
            item.IsEditingOverride = true;
        }
    }

    [RelayCommand]
    private void Continue()
    {
        if (CanContinue)
        {
            Step = DayEndStep.Summary;
        }
    }

    [RelayCommand]
    private void Back() => Step = DayEndStep.Checks;

    [RelayCommand]
    private async Task AddCollectionAsync()
    {
        if (Day is not { } day || !Money.TryParse(CollectionAmount, out var amount) || amount <= 0 || string.IsNullOrWhiteSpace(CollectionMethod))
        {
            ErrorMessage = "Enter how the money came in and an amount above zero.";
            return;
        }

        await ExecuteAsync(async () =>
        {
            var added = await _cashApi.AddCollectionAsync(day, new AddNonTillCollectionRequest
            {
                Method = CollectionMethod.Trim(),
                Amount = amount,
                Reference = string.IsNullOrWhiteSpace(CollectionReference) ? null : CollectionReference.Trim(),
            });
            if (!added.Success)
            {
                ErrorMessage = ApiFailures.Describe(added);
                return;
            }

            CollectionAmount = string.Empty;
            CollectionReference = string.Empty;
            await LoadAsync(day);
        });
    }

    [RelayCommand]
    private async Task RunAsync()
    {
        if (Day is not { } day || !CanContinue)
        {
            return;
        }

        Step = DayEndStep.Running;
        await ExecuteAsync(async () =>
        {
            var result = await _cashApi.RunDayEndAsync(new RunDayEndRequest
            {
                BusinessDay = day,
                Overrides = Items.Select(i => new DayEndOverrideDto { Code = i.Item.Code, Reason = i.OverrideReason.Trim() }).ToList(),
            });
            if (!result.Success || result.Data is null)
            {
                Step = DayEndStep.Summary;
                ErrorMessage = result.IsConnectionFailure
                    ? "No answer from the server. Check Day End history before running it again: the day may already be closed."
                    : ApiFailures.Describe(result);
                return;
            }

            Result = result.Data;
            OnPropertyChanged(nameof(ResultFiles));
            Step = DayEndStep.Done;
            _notifications.Success($"Business day {day.ToString("dd-MM-yyyy", CultureInfo.CurrentCulture)} is closed.");
        });
    }

    [RelayCommand]
    private Task OpenHistoryAsync() => _navigation.NavigateToAsync(ModuleRegistry.DayEndHistory);
}
