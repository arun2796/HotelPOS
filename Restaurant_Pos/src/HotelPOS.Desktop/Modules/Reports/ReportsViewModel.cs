using System.Collections.ObjectModel;
using System.Data;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Reports;
using HotelPOS.Desktop.Modules.Common;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Modules.Reports;

public enum RangePreset
{
    Today,
    Yesterday,
    ThisWeek,
    ThisMonth,
    LastMonth,
    Custom,
}

public sealed record ReportDefinition(string Key, string Title, string Description, bool IsMonthly = false);

public sealed partial class ReportsViewModel : ObservableObject, INavigationAware
{
    public static readonly IReadOnlyList<ReportDefinition> Catalogue = new[]
    {
        new ReportDefinition(ReportKeys.DailySales, "Daily sales", "Bills, covers, discounts, tax and net sales per business day"),
        new ReportDefinition(ReportKeys.MonthlySales, "Monthly sales", "The same figures per month of a year", IsMonthly: true),
        new ReportDefinition(ReportKeys.ItemSales, "Item sales", "Quantity and amount per menu item"),
        new ReportDefinition(ReportKeys.CategorySales, "Category sales", "Quantity and amount per category"),
        new ReportDefinition(ReportKeys.Payments, "Payments", "Amount per payment method, refunds netted"),
        new ReportDefinition(ReportKeys.Cancellations, "Cancellations", "Cancelled orders with reason and who cancelled"),
        new ReportDefinition(ReportKeys.Discounts, "Discounts", "Every discounted bill and who approved it"),
        new ReportDefinition(ReportKeys.Taxes, "Tax collection", "Taxable amount and tax per rate"),
        new ReportDefinition(ReportKeys.Waiters, "Waiter performance", "Orders, covers, sales and cancellations per waiter"),
        new ReportDefinition(ReportKeys.Kitchen, "Kitchen performance", "Tickets, accept and preparation times per station"),
    };

    private readonly IReportsApi _api;
    private readonly ISystemApi _systemApi;
    private readonly IFilePicker _files;
    private readonly INotificationService _notifications;
    private TimeOnly _businessDayStart = new(4, 0);

    public ReportsViewModel(IReportsApi api, ISystemApi systemApi, IFilePicker files, INotificationService notifications)
    {
        _api = api;
        _systemApi = systemApi;
        _files = files;
        _notifications = notifications;
        _selectedReport = Catalogue[0];
        var today = Today();
        _fromDate = today.ToDateTime(TimeOnly.MinValue);
        _toDate = _fromDate;
        _year = today.Year;
    }

    public IReadOnlyList<ReportDefinition> Reports => Catalogue;

    public IReadOnlyList<RangePreset> Presets { get; } = Enum.GetValues<RangePreset>();

    public IReadOnlyList<int> Years { get; } = Enumerable.Range(DateTime.Now.Year - 4, 5).Reverse().ToList();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMonthly), nameof(IsRange), nameof(Description))]
    private ReportDefinition _selectedReport;

    [ObservableProperty]
    private RangePreset _preset = RangePreset.Today;

    [ObservableProperty]
    private DateTime _fromDate;

    [ObservableProperty]
    private DateTime _toDate;

    [ObservableProperty]
    private int _year;

    [ObservableProperty]
    private DataTable? _table;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isBusy;

    public bool IsMonthly => SelectedReport.IsMonthly;

    public bool IsRange => !SelectedReport.IsMonthly;

    public bool IsCustom => Preset == RangePreset.Custom;

    public string Description => SelectedReport.Description;

    public ObservableCollection<string> Totals { get; } = new();

    public async Task OnNavigatedToAsync(object? parameter)
    {
        var settings = await _systemApi.GetPublicSettingsAsync();
        if (TimeOnly.TryParseExact(settings.Data?.FirstOrDefault(s => s.Key == SettingKeys.BusinessDayStartTime)?.Value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
        {
            _businessDayStart = start;
        }

        ApplyPreset(Preset);
        await RunAsync();
    }

    public void OnNavigatedFrom()
    {
    }

    // Business days, so at 01:00 "today" is still yesterday's calendar date.
    public static (DateOnly From, DateOnly To) PresetRange(RangePreset preset, DateOnly today)
    {
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var weekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        return preset switch
        {
            RangePreset.Yesterday => (today.AddDays(-1), today.AddDays(-1)),
            RangePreset.ThisWeek => (weekStart, today),
            RangePreset.ThisMonth => (monthStart, today),
            RangePreset.LastMonth => (monthStart.AddMonths(-1), monthStart.AddDays(-1)),
            _ => (today, today),
        };
    }

    partial void OnPresetChanged(RangePreset value)
    {
        OnPropertyChanged(nameof(IsCustom));
        if (value != RangePreset.Custom)
        {
            ApplyPreset(value);
            _ = RunAsync();
        }
    }

    partial void OnSelectedReportChanged(ReportDefinition value) => _ = RunAsync();

    partial void OnYearChanged(int value) => _ = RunAsync();

    [RelayCommand]
    public async Task RunAsync()
    {
        if (IsRange && ToDate.Date < FromDate.Date)
        {
            ErrorMessage = "The end date is before the start date.";
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var from = DateOnly.FromDateTime(FromDate);
            var to = DateOnly.FromDateTime(ToDate);
            var loaded = SelectedReport.Key switch
            {
                ReportKeys.DailySales => await LoadAsync(_api.GetRowsAsync<DailySalesRowDto>(ReportKeys.DailySales, from, to)),
                ReportKeys.MonthlySales => await LoadAsync(_api.GetMonthlyAsync(Year)),
                ReportKeys.ItemSales => await LoadAsync(_api.GetRowsAsync<ItemSalesRowDto>(ReportKeys.ItemSales, from, to)),
                ReportKeys.CategorySales => await LoadAsync(_api.GetRowsAsync<CategorySalesRowDto>(ReportKeys.CategorySales, from, to)),
                ReportKeys.Payments => await LoadAsync(_api.GetRowsAsync<PaymentSummaryRowDto>(ReportKeys.Payments, from, to)),
                ReportKeys.Cancellations => await LoadAsync(_api.GetRowsAsync<CancelledOrderRowDto>(ReportKeys.Cancellations, from, to)),
                ReportKeys.Discounts => await LoadAsync(_api.GetRowsAsync<DiscountRowDto>(ReportKeys.Discounts, from, to)),
                ReportKeys.Taxes => await LoadAsync(_api.GetRowsAsync<TaxCollectionRowDto>(ReportKeys.Taxes, from, to)),
                ReportKeys.Waiters => await LoadAsync(_api.GetRowsAsync<WaiterPerformanceRowDto>(ReportKeys.Waiters, from, to)),
                _ => await LoadAsync(_api.GetRowsAsync<KitchenPerformanceRowDto>(ReportKeys.Kitchen, from, to)),
            };
            if (!loaded)
            {
                return;
            }

            SummaryText = IsMonthly
                ? $"{SelectedReport.Title} · {Year} · {Table!.Rows.Count} rows"
                : $"{SelectedReport.Title} · {FromDate:d MMM yyyy} – {ToDate:d MMM yyyy} · {Table!.Rows.Count} rows";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        var from = IsMonthly ? (DateOnly?)null : DateOnly.FromDateTime(FromDate);
        var to = IsMonthly ? (DateOnly?)null : DateOnly.FromDateTime(ToDate);
        var suggested = IsMonthly
            ? $"{SelectedReport.Key.Replace('/', '-')}-{Year}.csv"
            : $"{SelectedReport.Key.Replace('/', '-')}-{from:yyyy-MM-dd}-to-{to:yyyy-MM-dd}.csv";
        var path = _files.PickSavePath(suggested, "CSV (comma separated)|*.csv");
        if (path is null)
        {
            return;
        }

        var result = await _api.DownloadCsvAsync(SelectedReport.Key, from, to, IsMonthly ? Year : null);
        if (!result.Success || result.Data is null)
        {
            _notifications.Error(ApiFailures.Describe(result), result.CorrelationId);
            return;
        }

        try
        {
            await File.WriteAllBytesAsync(path, result.Data);
            _notifications.Success($"Saved {Path.GetFileName(path)}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _notifications.Error($"Could not save the file: {ex.Message}");
        }
    }

    private void ApplyPreset(RangePreset preset)
    {
        if (preset == RangePreset.Custom)
        {
            return;
        }

        var (from, to) = PresetRange(preset, Today());
        FromDate = from.ToDateTime(TimeOnly.MinValue);
        ToDate = to.ToDateTime(TimeOnly.MinValue);
    }

    private DateOnly Today()
    {
        var now = DateTime.Now;
        return DateOnly.FromDateTime(TimeOnly.FromDateTime(now) < _businessDayStart ? now.AddDays(-1) : now);
    }

    private async Task<bool> LoadAsync<T>(Task<ApiResult<List<T>>> pending)
    {
        var result = await pending;
        if (!result.Success || result.Data is null)
        {
            ErrorMessage = result.IsConnectionFailure ? "Connection unavailable." : ApiFailures.Describe(result);
            return false;
        }

        Table = BuildTable(result.Data);
        return true;
    }

    // Every column is text so one grid can show any report; totals follow the same schema as the CSV.
    private DataTable BuildTable<T>(IReadOnlyList<T> rows)
    {
        var columns = ReportSchema.Describe<T>();
        var table = new DataTable();
        foreach (var column in columns)
        {
            table.Columns.Add(column.Header, typeof(string));
        }

        foreach (var row in rows)
        {
            table.Rows.Add(columns.Select(c => (object)ReportSchema.Format(c.Property.GetValue(row))).ToArray());
        }

        Totals.Clear();
        if (rows.Count > 0)
        {
            foreach (var column in columns.Where(c => c.HasTotal))
            {
                Totals.Add($"{column.Header}: {ReportSchema.Format(TotalOf(rows, column))}");
            }
        }

        return table;
    }

    private static object TotalOf<T>(IReadOnlyList<T> rows, ReportColumn column)
    {
        var type = Nullable.GetUnderlyingType(column.Property.PropertyType) ?? column.Property.PropertyType;
        if (type == typeof(int) || type == typeof(long))
        {
            return rows.Sum(r => Convert.ToInt64(column.Property.GetValue(r) ?? 0, CultureInfo.InvariantCulture));
        }

        return rows.Sum(r => Convert.ToDecimal(column.Property.GetValue(r) ?? 0m, CultureInfo.InvariantCulture));
    }
}
