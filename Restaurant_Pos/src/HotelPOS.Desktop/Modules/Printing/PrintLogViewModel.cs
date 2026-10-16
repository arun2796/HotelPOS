using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Print;
using HotelPOS.Desktop.Modules.Common;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Configuration;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Printing;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Modules.Printing;

public enum PrintLogFilter
{
    All,
    Failed,
}

public sealed record PrintLogFilterOption(PrintLogFilter Filter, string Label);

// Opens the print log; the cashier tile and the bell pass Failed.
public sealed record PrintLogContext(PrintLogFilter Filter);

public sealed record PrintLogRow(PrintLogDto Copy, string Title, bool IsLatestCopy)
{
    public bool IsFailed => Copy.Result == PrintResult.Failed;

    // A failed copy is retried once: a later copy of the same document supersedes it. PDFs have nothing to retry.
    public bool CanRetry => IsFailed && IsLatestCopy && Copy.Output == PrintOutput.Printer;

    public string WhenText => Copy.CreatedAtUtc.ToLocalTime().ToString("dd MMM HH:mm", CultureInfo.CurrentCulture);

    public string CopyText => Copy.CopyNumber <= 1 ? "Original" : $"Copy {Copy.CopyNumber} · {TemplateLimits.DuplicateLabel}";

    public string StatusText => Copy.Result switch
    {
        PrintResult.Printed => "Printed",
        PrintResult.Failed => IsLatestCopy ? "Failed" : "Failed (retried)",
        _ => "Waiting",
    };

    public string WhereText => Copy.Output == PrintOutput.Pdf ? "PDF" : Copy.PrinterName ?? Copy.DeviceName ?? string.Empty;
}

// Phase 20.8: the copies of the documents this terminal failed to print, with a Failed filter and a Retry per failed
// copy. No endpoint lists the print log across documents (the server only has per-document history, and 20.8 adds no
// endpoint), so the screen starts from this terminal's failed jobs in the print queue and reads each document's history;
// Retry goes through DocumentPrinter.PrintAsync (ReserveCopy, render, CompletePrint), so it takes the next copy number
// and prints DUPLICATE.
public sealed partial class PrintLogViewModel : ViewModelBase, INavigationAware, IRefreshable, IDisposable
{
    private readonly IPrintApi _api;
    private readonly IPrintQueue _queue;
    private readonly IDocumentPrinter _printer;
    private readonly IClientSettingsService _settings;
    private readonly IUiDispatcher _ui;
    private readonly HashSet<(PrintDocumentType Type, int Id)> _documents = new();
    private readonly Dictionary<long, PrintOutcome> _outcomes = new();
    private readonly List<PrintLogRow> _all = new();
    private bool _subscribed;

    public PrintLogViewModel(IPrintApi api, IPrintQueue queue, IDocumentPrinter printer, IClientSettingsService settings, IUiDispatcher ui)
    {
        _api = api;
        _queue = queue;
        _printer = printer;
        _settings = settings;
        _ui = ui;
        _selectedFilter = FilterOptions[0];
    }

    public IReadOnlyList<PrintLogFilterOption> FilterOptions { get; } = new[]
    {
        new PrintLogFilterOption(PrintLogFilter.All, "All copies"),
        new PrintLogFilterOption(PrintLogFilter.Failed, "Failed"),
    };

    public ObservableCollection<PrintLogRow> Rows { get; } = new();

    [ObservableProperty]
    private PrintLogFilterOption _selectedFilter;

    [ObservableProperty]
    private string _statusText = string.Empty;

    public bool IsEmpty => Rows.Count == 0;

    public string EmptyText => SelectedFilter.Filter == PrintLogFilter.Failed
        ? "Nothing failed to print on this terminal."
        : "No prints on this terminal need attention. Failed prints appear here with a Retry.";

    public Task OnNavigatedToAsync(object? parameter)
    {
        if (!_subscribed)
        {
            _queue.Changed += OnQueueChanged;
            _queue.Completed += OnCompleted;
            _subscribed = true;
        }

        var filter = (parameter as PrintLogContext)?.Filter ?? PrintLogFilter.All;
        SelectedFilter = FilterOptions.First(o => o.Filter == filter);
        return RefreshAsync();
    }

    public void OnNavigatedFrom() => Dispose();

    public void Dispose()
    {
        if (_subscribed)
        {
            _queue.Changed -= OnQueueChanged;
            _queue.Completed -= OnCompleted;
            _subscribed = false;
        }
    }

    partial void OnSelectedFilterChanged(PrintLogFilterOption value) => Apply();

    public async Task RefreshAsync()
    {
        foreach (var failed in _queue.Failed)
        {
            _documents.Add((failed.Job.Type, failed.Job.EntityId));
        }

        var rows = new List<PrintLogRow>();
        foreach (var (type, id) in _documents)
        {
            var history = await _api.GetHistoryAsync(type, id);
            if (history.Data is not { } copies)
            {
                ErrorMessage = history.IsConnectionFailure ? "Connection unavailable. The list refreshes when the server is back." : history.Message;
                continue;
            }

            ErrorMessage = null;
            var latest = copies.Count == 0 ? 0 : copies.Max(c => c.CopyNumber);
            foreach (var copy in copies.Where(IsThisTerminal))
            {
                rows.Add(new PrintLogRow(WithLocalResult(copy), TitleOf(type, id, copy.Id), copy.CopyNumber == latest));
            }
        }

        _all.Clear();
        _all.AddRange(rows.OrderByDescending(r => r.Copy.CreatedAtUtc).ThenByDescending(r => r.Copy.CopyNumber));
        Apply();
    }

    // The failed copy is retried here: the next copy is reserved and printed through the normal pipeline.
    [RelayCommand]
    private async Task RetryAsync(PrintLogRow? row)
    {
        if (row is null || !row.CanRetry)
        {
            return;
        }

        StatusText = $"Retrying {row.Title}…";
        var queued = await _printer.PrintAsync(row.Copy.DocumentType, row.Copy.DocumentId);
        if (!queued)
        {
            StatusText = $"{row.Title} was not retried. See the notice for why.";
            return;
        }

        // The failed copy keeps its number in the log; the status bar no longer needs to offer it.
        if (_queue.Failed.FirstOrDefault(f => f.Job.PrintLogId == row.Copy.Id) is { } entry)
        {
            _queue.Discard(entry);
        }

        StatusText = $"{row.Title}: next copy reserved, printing…";
        await RefreshAsync();
    }

    [RelayCommand]
    private Task ReloadAsync() => RefreshAsync();

    private bool IsThisTerminal(PrintLogDto copy) =>
        string.IsNullOrEmpty(copy.DeviceName) || string.Equals(copy.DeviceName, _settings.Current.DeviceName, StringComparison.OrdinalIgnoreCase);

    // The terminal reports a result to the server in the background; what this terminal already knows wins until then.
    private PrintLogDto WithLocalResult(PrintLogDto copy)
    {
        if (copy.Result != PrintResult.Reserved)
        {
            return copy;
        }

        if (_outcomes.TryGetValue(copy.Id, out var outcome))
        {
            return copy with { Result = outcome.Succeeded ? PrintResult.Printed : PrintResult.Failed, Error = outcome.Error };
        }

        return _queue.Failed.FirstOrDefault(f => f.Job.PrintLogId == copy.Id) is { } failed
            ? copy with { Result = PrintResult.Failed, Error = failed.Error }
            : copy;
    }

    private string TitleOf(PrintDocumentType type, int id, long logId) =>
        _queue.Failed.FirstOrDefault(f => f.Job.PrintLogId == logId)?.Job.Title
        ?? (_outcomes.TryGetValue(logId, out var outcome) ? outcome.Job.Title : $"{DocumentPrinter.Label(type)} #{id}");

    private void Apply()
    {
        var wanted = SelectedFilter.Filter == PrintLogFilter.Failed ? _all.Where(r => r.IsFailed) : _all;
        Rows.Clear();
        foreach (var row in wanted)
        {
            Rows.Add(row);
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyText));
    }

    private void OnQueueChanged(object? sender, EventArgs e) => _ui.Post(() => _ = RefreshAsync());

    private void OnCompleted(object? sender, PrintOutcome outcome) => _ui.Post(() =>
    {
        if (outcome.Job.PrintLogId is { } logId)
        {
            _outcomes[logId] = outcome;
        }

        if (!outcome.Succeeded)
        {
            _documents.Add((outcome.Job.Type, outcome.Job.EntityId));
        }

        if (_documents.Contains((outcome.Job.Type, outcome.Job.EntityId)))
        {
            StatusText = outcome.Succeeded
                ? $"{outcome.Job.Title} printed."
                : $"{outcome.Job.Title} failed again: {outcome.Error}";
            _ = RefreshAsync();
        }
    });
}
