using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Admin;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Modules.Admin;

public sealed partial class AuditLogsViewModel : ObservableObject, INavigationAware, IRefreshable
{
    private const int PageSize = 50;

    private readonly IAuditApi _auditApi;
    private readonly IFilePicker _filePicker;
    private readonly INotificationService _notifications;

    public AuditLogsViewModel(IAuditApi auditApi, IFilePicker filePicker, INotificationService notifications)
    {
        _auditApi = auditApi;
        _filePicker = filePicker;
        _notifications = notifications;
    }

    public ObservableCollection<AuditLogDto> AuditEntries { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(FormattedOldValues), nameof(FormattedNewValues))]
    private AuditLogDto? _selectedEntry;

    [ObservableProperty]
    private string _search = string.Empty;

    [ObservableProperty]
    private string _actionFilter = string.Empty;

    [ObservableProperty]
    private string _entityTypeFilter = string.Empty;

    [ObservableProperty]
    private string _dateRange = "Today";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageText))]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand), nameof(NextPageCommand))]
    private int _page = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageText))]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    private int _totalPages = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageText))]
    private int _totalCount;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    public bool HasSelection => SelectedEntry is not null;

    public string PageText => $"Page {Page} of {Math.Max(1, TotalPages)} · {TotalCount} audit record(s)";

    public string FormattedOldValues => FormatJson(SelectedEntry?.OldValues);
    public string FormattedNewValues => FormatJson(SelectedEntry?.NewValues);

    public Task OnNavigatedToAsync(object? parameter) => LoadAsync();
    public void OnNavigatedFrom() { }
    [RelayCommand]
    public Task RefreshAsync() => LoadAsync();

    partial void OnDateRangeChanged(string value)
    {
        Page = 1;
        _ = LoadAsync();
    }

    private (DateTime? FromUtc, DateTime? ToUtc) GetDateFilter()
    {
        var now = DateTime.UtcNow;
        return DateRange switch
        {
            "Today" => (DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(1).AddTicks(-1)),
            "Last 7 Days" => (now.AddDays(-7), now),
            _ => (null, null),
        };
    }

    private AuditLogQuery BuildQuery()
    {
        var (fromUtc, toUtc) = GetDateFilter();
        return new AuditLogQuery
        {
            Page = Page,
            PageSize = PageSize,
            Action = string.IsNullOrWhiteSpace(ActionFilter) ? null : ActionFilter.Trim(),
            EntityType = string.IsNullOrWhiteSpace(EntityTypeFilter) ? null : EntityTypeFilter.Trim(),
            Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
            From = fromUtc,
            To = toUtc,
        };
    }

    private async Task LoadAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _auditApi.QueryAsync(BuildQuery());
            if (!result.Success || result.Data is null)
            {
                ErrorMessage = result.Message;
                return;
            }

            AuditEntries.Clear();
            foreach (var item in result.Data.Items)
            {
                AuditEntries.Add(item);
            }

            TotalCount = result.Data.TotalCount;
            TotalPages = Math.Max(1, result.Data.TotalPages);
            SelectedEntry = AuditEntries.FirstOrDefault();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task SearchAsync()
    {
        Page = 1;
        return LoadAsync();
    }

    private bool CanGoBack() => Page > 1;

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private Task PreviousPageAsync()
    {
        Page--;
        return LoadAsync();
    }

    private bool CanGoForward() => Page < TotalPages;

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private Task NextPageAsync()
    {
        Page++;
        return LoadAsync();
    }

    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        var defaultFileName = $"audit_log_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
        var savePath = _filePicker.PickSavePath(defaultFileName, "CSV (*.csv)|*.csv");
        if (string.IsNullOrWhiteSpace(savePath)) return;

        IsBusy = true;
        try
        {
            var result = await _auditApi.ExportCsvAsync(BuildQuery());
            if (result.Success && result.Data is not null)
            {
                await File.WriteAllBytesAsync(savePath, result.Data);
                _notifications.Success($"Audit log exported ({result.Data.Length:N0} bytes) to {Path.GetFileName(savePath)}.");
            }
            else
            {
                _notifications.Error(result.Message, result.CorrelationId);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string FormatJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return "—";
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return json;
        }
    }
}
