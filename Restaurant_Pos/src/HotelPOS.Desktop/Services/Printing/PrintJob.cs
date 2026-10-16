using HotelPOS.Contracts.Print;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Configuration;
using Microsoft.Extensions.Logging;

namespace HotelPOS.Desktop.Services.Printing;

public sealed record PrintJob(PrintDocumentType Type, int EntityId, string Title, object Document, PrinterProfile Profile, int Copies)
{
    public Guid Id { get; } = Guid.NewGuid();

    public bool NotifyOnSuccess { get; init; }

    // Phase 14: the server's print log entry of this copy; the queue reports whether it came out.
    public long? PrintLogId { get; init; }

    // The number people know the document by (invoice or ticket number); null before an invoice is issued.
    public string? DocumentNumber { get; init; }

    // Reserves the next copy for a manual retry: a failed copy keeps its number, the retry takes the next one.
    public Func<Task<PrintJob?>>? Renew { get; init; }
}

public interface IPrintCopyReporter
{
    Task ReportAsync(long printLogId, bool succeeded, string? error);
}

// Tells the server how a reserved copy went. A report that does not arrive only leaves the copy "reserved" in the log;
// printing never waits for it.
public sealed class PrintCopyReporter : IPrintCopyReporter
{
    private readonly IPrintApi _api;
    private readonly ILogger<PrintCopyReporter> _logger;

    public PrintCopyReporter(IPrintApi api, ILogger<PrintCopyReporter> logger)
    {
        _api = api;
        _logger = logger;
    }

    public async Task ReportAsync(long printLogId, bool succeeded, string? error)
    {
        try
        {
            var result = await _api.CompleteCopyAsync(printLogId, new CompletePrintRequest { Succeeded = succeeded, Error = error });
            if (!result.Success)
            {
                _logger.LogWarning("Print log entry {Id} was not updated: {Message}", printLogId, result.Message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Print log entry {Id} was not updated", printLogId);
        }
    }
}

public sealed class PrintException : Exception
{
    public PrintException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

public interface IPrinterProfiles
{
    PrinterProfile? For(PrintDocumentType type);

    bool AutoPrint(PrintDocumentType type);
}

public sealed class PrinterProfiles : IPrinterProfiles
{
    private readonly IClientSettingsService _settings;

    public PrinterProfiles(IClientSettingsService settings)
    {
        _settings = settings;
    }

    public PrinterProfile? For(PrintDocumentType type) =>
        _settings.Current.Printers.TryGetValue(type, out var profile) && IsUsable(profile) ? profile : null;

    public bool AutoPrint(PrintDocumentType type)
    {
        var settings = _settings.Current;
        return type switch
        {
            PrintDocumentType.Kot => settings.AutoPrintKot,
            PrintDocumentType.Invoice => settings.AutoPrintInvoice,
            PrintDocumentType.Receipt => settings.AutoPrintReceipt,
            _ => false,
        };
    }

    public static bool IsUsable(PrinterProfile profile) => profile.Kind switch
    {
        PrinterKind.Network => !string.IsNullOrWhiteSpace(profile.Host) && profile.Port is > 0 and <= 65535,
        PrinterKind.EscPosRaw => !string.IsNullOrWhiteSpace(profile.PrinterName),
        _ => true,
    };
}
