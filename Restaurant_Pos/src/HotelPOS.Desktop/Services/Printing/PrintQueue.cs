using System.Threading.Channels;
using HotelPOS.Contracts.Print;
using HotelPOS.Desktop.Services.Ui;
using Microsoft.Extensions.Logging;

namespace HotelPOS.Desktop.Services.Printing;

public interface IPrintService
{
    Task PrintAsync(PrintJob job, CancellationToken cancellationToken = default);
}

public sealed class PrintService : IPrintService
{
    private readonly EscPosRenderer _escPos;
    private readonly RawPrinterChannel _raw;
    private readonly NetworkPrinterChannel _network;
    private readonly IWindowsPrinterChannel _windows;

    public PrintService(EscPosRenderer escPos, RawPrinterChannel raw, NetworkPrinterChannel network, IWindowsPrinterChannel windows)
    {
        _escPos = escPos;
        _raw = raw;
        _network = network;
        _windows = windows;
    }

    public async Task PrintAsync(PrintJob job, CancellationToken cancellationToken = default)
    {
        var profile = job.Profile;
        var copies = Math.Clamp(job.Copies, 1, PrintLimits.MaxCopies);
        if (profile.Kind == PrinterKind.Windows)
        {
            await _windows.PrintAsync(job.Document, profile, copies, cancellationToken);
            return;
        }

        var bytes = _escPos.Render(job.Document, profile);
        IEscPosChannel channel = profile.Kind == PrinterKind.Network ? _network : _raw;
        for (var copy = 0; copy < copies; copy++)
        {
            await channel.SendAsync(bytes, profile, cancellationToken);
        }
    }
}

public sealed record FailedPrintJob(PrintJob Job, string Error, DateTime FailedAtUtc);

public sealed class PrintQueueOptions
{
    public IReadOnlyList<TimeSpan> RetryDelays { get; init; } = new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5) };

    public TimeSpan KeepFailedFor { get; init; } = TimeSpan.FromMinutes(10);
}

public interface IPrintQueue
{
    event EventHandler? Changed;

    int FailedCount { get; }

    IReadOnlyList<FailedPrintJob> Failed { get; }

    void Enqueue(PrintJob job);

    void Retry(FailedPrintJob failed);

    void RetryAll();

    void Discard(FailedPrintJob failed);

    void DiscardAll();
}

// One job at a time, in order, on a background loop: a jammed kitchen printer slows printing down, never the order.
public sealed class PrintQueue : IPrintQueue, IDisposable
{
    private readonly IPrintService _printer;
    private readonly INotificationService _notifications;
    private readonly ILogger<PrintQueue> _logger;
    private readonly PrintQueueOptions _options;
    private readonly Channel<PrintJob> _jobs = Channel.CreateUnbounded<PrintJob>();
    private readonly List<FailedPrintJob> _failed = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _worker;

    public PrintQueue(IPrintService printer, INotificationService notifications, ILogger<PrintQueue> logger, PrintQueueOptions? options = null)
    {
        _printer = printer;
        _notifications = notifications;
        _logger = logger;
        _options = options ?? new PrintQueueOptions();
        _worker = Task.Run(() => RunAsync(_stopping.Token));
    }

    public event EventHandler? Changed;

    public int FailedCount
    {
        get
        {
            lock (_failed)
            {
                return _failed.Count;
            }
        }
    }

    public IReadOnlyList<FailedPrintJob> Failed
    {
        get
        {
            lock (_failed)
            {
                return _failed.ToList();
            }
        }
    }

    public void Enqueue(PrintJob job)
    {
        Prune();
        _jobs.Writer.TryWrite(job);
    }

    public void Retry(FailedPrintJob failed)
    {
        Remove(failed);
        _jobs.Writer.TryWrite(failed.Job with { NotifyOnSuccess = true });
    }

    public void RetryAll()
    {
        foreach (var failed in Failed)
        {
            Retry(failed);
        }
    }

    public void Discard(FailedPrintJob failed) => Remove(failed);

    public void DiscardAll()
    {
        lock (_failed)
        {
            _failed.Clear();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _stopping.Cancel();
        _jobs.Writer.TryComplete();
        _stopping.Dispose();
    }

    internal Task Idle => _worker;

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var job in _jobs.Reader.ReadAllAsync(cancellationToken))
            {
                await ProcessAsync(job, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ProcessAsync(PrintJob job, CancellationToken cancellationToken)
    {
        string? lastError = null;
        for (var attempt = 0; attempt <= _options.RetryDelays.Count; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(_options.RetryDelays[attempt - 1], cancellationToken);
            }

            try
            {
                await _printer.PrintAsync(job, cancellationToken);
                _logger.LogInformation("Printed {Title} on {Printer} (attempt {Attempt})", job.Title, job.Profile.Target, attempt + 1);
                if (job.NotifyOnSuccess)
                {
                    _notifications.Success($"{job.Title} printed.");
                }

                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                lastError = ex is PrintException ? ex.Message : $"Printing failed: {ex.Message}";
                _logger.LogWarning(ex, "Print attempt {Attempt} of {Title} on {Printer} failed", attempt + 1, job.Title, job.Profile.Target);
            }
        }

        var failed = new FailedPrintJob(job, lastError ?? "Printing failed.", DateTime.UtcNow);
        lock (_failed)
        {
            _failed.Add(failed);
        }

        _logger.LogError("{Title} could not be printed on {Printer}: {Error}", job.Title, job.Profile.Target, failed.Error);
        _notifications.Error($"{job.Title} was not printed. {failed.Error}", "Retry", () => Retry(failed));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Remove(FailedPrintJob failed)
    {
        bool removed;
        lock (_failed)
        {
            removed = _failed.Remove(failed);
        }

        if (removed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Prune()
    {
        var cutoff = DateTime.UtcNow - _options.KeepFailedFor;
        int removed;
        lock (_failed)
        {
            removed = _failed.RemoveAll(f => f.FailedAtUtc < cutoff);
        }

        if (removed > 0)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
