using System.Data;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Gst;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Reports;
using HotelPOS.Desktop.Modules.Billing;
using HotelPOS.Desktop.Modules.Common;
using HotelPOS.Desktop.Modules.Reports;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Realtime;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Modules.Gst;

// The GST returns of one month: GSTR-1 (with its sections), GSTR-3B, the HSN/SAC summary and the validation report,
// each exportable as Excel or PDF; the GSTR-1 as the portal's JSON file.
public sealed partial class GstHubViewModel : ViewModelBase, INavigationAware
{
    public const int Gstr1Tab = 0;
    public const int Gstr3bTab = 1;
    public const int HsnTab = 2;
    public const int ValidationTab = 3;

    private static readonly string[] ReportRoutes = { "gstr1", "gstr3b", "hsn-summary", "validation" };

    private readonly IGstApi _api;
    private readonly IFilePicker _files;
    private readonly INotificationService _notifications;
    private readonly IRealtimeClient _realtime;
    private readonly IUiDispatcher _ui;
    private IDisposable? _periodSubscription;
    private Gstr1Dto? _gstr1;
    private Gstr3bDto? _gstr3b;
    private IReadOnlyList<HsnSummaryRowDto> _hsn = Array.Empty<HsnSummaryRowDto>();
    private GstValidationDto? _validation;

    public GstHubViewModel(IGstApi api, IFilePicker files, INotificationService notifications, IRealtimeClient realtime, IUiDispatcher ui)
    {
        _api = api;
        _files = files;
        _notifications = notifications;
        _realtime = realtime;
        _ui = ui;
        // The current month: that is where the day's invoices are; the previous month is one click away.
        _year = DateTime.Today.Year;
        _month = DateTime.Today.Month;
    }

    public IReadOnlyList<string> Tabs { get; } = new[] { "GSTR-1", "GSTR-3B", "HSN summary", "Validation" };

    public IReadOnlyList<string> Sections { get; } = new[] { "B2B invoices", "B2CL invoices", "B2CS summary", "Credit notes", "HSN summary", "Documents issued" };

    public IReadOnlyList<int> Years { get; } = Enumerable.Range(DateTime.Now.Year - 4, 6).Reverse().ToList();

    public IReadOnlyList<string> MonthNames { get; } = Enumerable.Range(1, 12)
        .Select(m => CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(m)).ToList();

    [ObservableProperty]
    private int _year;

    // 1 to 12.
    [ObservableProperty]
    private int _month;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGstr1Tab))]
    private int _selectedTab;

    [ObservableProperty]
    private int _selectedSection;

    [ObservableProperty]
    private DataTable? _table;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    private string? _validationBanner;

    public int MonthIndex
    {
        get => Month - 1;
        set
        {
            if (value >= 0)
            {
                Month = value + 1;
            }
        }
    }

    partial void OnMonthChanged(int value) => OnPropertyChanged(nameof(MonthIndex));

    public bool IsGstr1Tab => SelectedTab == Gstr1Tab;

    public string Period => $"{Year:0000}-{Month:00}";

    public IReadOnlySet<string> NumericColumns { get; private set; } = new HashSet<string>();

    public bool HasNoRows => Table is { Rows.Count: 0 };

    public Task OnNavigatedToAsync(object? parameter)
    {
        // A period closed or reopened elsewhere changes the validation of the month on screen.
        _periodSubscription?.Dispose();
        _periodSubscription = _realtime.Subscribe<PeriodStatusChangedEvent>(HubEvents.PeriodStatusChanged, OnPeriodStatusChanged);
        return RunAsync();
    }

    public void OnNavigatedFrom()
    {
        _periodSubscription?.Dispose();
        _periodSubscription = null;
    }

    private void OnPeriodStatusChanged(PeriodStatusChangedEvent evt) => _ui.Post(() =>
    {
        var month = new DateOnly(Year, Month, 1);
        if (evt.From <= month && month <= evt.To)
        {
            _ = RunAsync();
        }
    });

    partial void OnSelectedTabChanged(int value) => Render();

    partial void OnSelectedSectionChanged(int value) => Render();

    [RelayCommand]
    private Task RunAsync() => ExecuteAsync(async () =>
    {
        var period = Period;
        var gstr1 = await FetchAsync(_api.GetGstr1Async(period));
        var gstr3b = await FetchAsync(_api.GetGstr3bAsync(period));
        var hsn = await FetchAsync(_api.GetHsnSummaryAsync(period));
        var validation = await FetchAsync(_api.GetValidationAsync(period));
        if (gstr1 is null || gstr3b is null || hsn is null || validation is null)
        {
            return;
        }

        (_gstr1, _gstr3b, _hsn, _validation) = (gstr1, gstr3b, hsn, validation);
        // A month without invoices has nothing to fix; the validation findings are still on their tab.
        var errors = validation.Issues.Count(i => i.Severity == GstIssueSeverity.Error);
        ValidationBanner = validation.CanFile || gstr1.Totals.Invoices == 0
            ? null
            : $"{Plural.Count(errors, "error")} must be fixed before this month can be filed or closed. See the Validation tab.";
        Render();
    });

    [RelayCommand]
    private Task ExportJsonAsync() => SaveAsync($"GSTR1_{Period}.json", "JSON file|*.json", () => _api.DownloadGstr1JsonAsync(Period));

    [RelayCommand]
    private Task ExportExcelAsync() => SaveAsync($"{ReportRoutes[SelectedTab]}-{Period}.xlsx", "Excel workbook|*.xlsx", () => _api.DownloadAsync(ReportRoutes[SelectedTab], Period, "xlsx"));

    [RelayCommand]
    private Task SavePdfAsync() => SaveAsync($"{ReportRoutes[SelectedTab]}-{Period}.pdf", "PDF document|*.pdf", () => _api.DownloadAsync(ReportRoutes[SelectedTab], Period, "pdf"));

    private async Task SaveAsync(string suggested, string filter, Func<Task<ApiResult<byte[]>>> download)
    {
        var path = _files.PickSavePath(suggested, filter);
        if (path is null)
        {
            return;
        }

        var result = await download();
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

    private void Render()
    {
        if (_gstr1 is null || _gstr3b is null || _validation is null)
        {
            return;
        }

        Table = SelectedTab switch
        {
            Gstr1Tab => SelectedSection switch
            {
                0 => Build(_gstr1.B2b),
                1 => Build(_gstr1.B2cl),
                2 => Build(_gstr1.B2cs),
                3 => Build(_gstr1.CreditNotes),
                4 => Build(_gstr1.Hsn),
                _ => Build(_gstr1.Documents),
            },
            Gstr3bTab => Build(_gstr3b.Rows),
            HsnTab => Build(_hsn),
            _ => Build(_validation.Issues),
        };
        var t = _gstr1.Totals;
        SummaryText = SelectedTab == Gstr1Tab
            ? $"{Tabs[SelectedTab]} · {Period} · {t.Invoices} invoices · invoice value {Money.Format(t.InvoiceValue)} · taxable {Money.Format(t.TaxableValue)} · IGST {Money.Format(t.Igst)} · CGST {Money.Format(t.Cgst)} · SGST {Money.Format(t.Sgst)} · credit notes tax {Money.Format(t.CreditNoteTax)}"
            : $"{Tabs[SelectedTab]} · {Period}";
        OnPropertyChanged(nameof(HasNoRows));
    }

    // Text columns following the same schema as the Excel and PDF exports, formatted for reading.
    private DataTable Build<T>(IReadOnlyList<T> rows)
    {
        var columns = ReportSchema.Describe<T>();
        var table = new DataTable();
        foreach (var column in columns)
        {
            table.Columns.Add(column.Header, typeof(string));
        }

        foreach (var row in rows)
        {
            table.Rows.Add(columns.Select(c => (object)ReportsViewModel.Display(c.Property.GetValue(row))).ToArray());
        }

        NumericColumns = columns.Where(c => c.IsNumeric).Select(c => c.Header).ToHashSet();
        return table;
    }
}
