using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Print;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Desktop.Modules.Common;
using HotelPOS.Desktop.Modules.Printing;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Configuration;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Realtime;
using HotelPOS.Desktop.Services.Ui;
using Microsoft.Extensions.Logging;

namespace HotelPOS.Desktop.Services.Printing;

public interface IDocumentPrinter
{
    bool CanPrint(PrintDocumentType type);

    // Fetches the document from the server and queues it; false when this terminal has no printer for it.
    Task<bool> PrintAsync(PrintDocumentType type, int entityId, bool quietWhenUnconfigured = false);

    Task AutoPrintAsync(PrintDocumentType type, int entityId);

    Task<bool> ReprintAsync(PrintDocumentType type, int entityId, string reason);

    Task PreviewAsync(PrintDocumentType type, int entityId, string returnModule);

    void Print(PrintJob job);

    Task TestPrintAsync(PrintDocumentType type, PrinterProfile profile);
}

public sealed class DocumentPrinter : IDocumentPrinter
{
    private readonly IPrintApi _api;
    private readonly IPrinterProfiles _profiles;
    private readonly IPrintQueue _queue;
    private readonly IPrintService _printer;
    private readonly INavigationService _navigation;
    private readonly INotificationService _notifications;
    private readonly IClientSettingsService _settings;

    public DocumentPrinter(
        IPrintApi api,
        IPrinterProfiles profiles,
        IPrintQueue queue,
        IPrintService printer,
        INavigationService navigation,
        INotificationService notifications,
        IClientSettingsService settings)
    {
        _api = api;
        _profiles = profiles;
        _queue = queue;
        _printer = printer;
        _navigation = navigation;
        _notifications = notifications;
        _settings = settings;
    }

    public bool CanPrint(PrintDocumentType type) => _profiles.For(type) is not null;

    public async Task<bool> PrintAsync(PrintDocumentType type, int entityId, bool quietWhenUnconfigured = false)
    {
        var profile = _profiles.For(type);
        if (profile is null)
        {
            if (!quietWhenUnconfigured)
            {
                _notifications.Warning($"No {Label(type).ToLowerInvariant()} printer is set up on this terminal (Printers in the menu).");
            }

            return false;
        }

        var job = await FetchAsync(type, entityId, profile);
        if (job is null)
        {
            return false;
        }

        _queue.Enqueue(job);
        return true;
    }

    public Task AutoPrintAsync(PrintDocumentType type, int entityId) =>
        _profiles.AutoPrint(type) ? PrintAsync(type, entityId, quietWhenUnconfigured: true) : Task.CompletedTask;

    public async Task<bool> ReprintAsync(PrintDocumentType type, int entityId, string reason)
    {
        if (!CanPrint(type))
        {
            return await PrintAsync(type, entityId);
        }

        if (type != PrintDocumentType.Kot)
        {
            var recorded = await _api.RecordReprintAsync(new ReprintRequest { DocumentType = type, EntityId = entityId, Reason = reason });
            if (!recorded.Success)
            {
                _notifications.Error(ApiFailures.Describe(recorded), recorded.CorrelationId);
                return false;
            }
        }

        return await PrintAsync(type, entityId);
    }

    public async Task PreviewAsync(PrintDocumentType type, int entityId, string returnModule)
    {
        var profile = _profiles.For(type) ?? new PrinterProfile { Kind = PrinterKind.Windows };
        var job = await FetchAsync(type, entityId, profile);
        if (job is not null)
        {
            await _navigation.NavigateToPageAsync<PrintPreviewViewModel>(new PrintPreviewContext(job, returnModule, CanPrint(type)));
        }
    }

    public void Print(PrintJob job) => _queue.Enqueue(job);

    public Task TestPrintAsync(PrintDocumentType type, PrinterProfile profile) =>
        _printer.PrintAsync(new PrintJob(type, 0, "Test page", new TestPrintDocument
        {
            DeviceName = _settings.Current.DeviceName,
            ProfileName = Label(type),
            Target = profile.Target,
            PrintedAtUtc = DateTime.UtcNow,
        }, profile, 1));

    public static string Label(PrintDocumentType type) => type switch
    {
        PrintDocumentType.Kot => "Kitchen ticket",
        PrintDocumentType.Invoice => "Invoice",
        _ => "Receipt",
    };

    private async Task<PrintJob?> FetchAsync(PrintDocumentType type, int entityId, PrinterProfile profile)
    {
        switch (type)
        {
            case PrintDocumentType.Kot:
                var kot = await _api.GetKotAsync(entityId);
                return kot.Data is { } ticket
                    ? new PrintJob(type, entityId, $"KOT {ticket.TicketNumber}", ticket, profile, profile.Copies)
                    : Fail(kot);
            case PrintDocumentType.Invoice:
                var invoice = await _api.GetInvoiceAsync(entityId);
                // The server's copy count (PrintInvoiceCopies) wins unless the terminal asks for more.
                return invoice.Data is { } bill
                    ? new PrintJob(type, entityId, $"Invoice {bill.InvoiceNumber ?? "#" + bill.BillNumber}", bill, profile, Math.Max(profile.Copies, bill.Copies))
                    : Fail(invoice);
            default:
                var receipt = await _api.GetReceiptAsync(entityId);
                return receipt.Data is { } payment
                    ? new PrintJob(type, entityId, $"Receipt {payment.InvoiceNumber ?? "#" + payment.BillId}", payment, profile, profile.Copies)
                    : Fail(receipt);
        }
    }

    private PrintJob? Fail<T>(ApiResult<T> result)
    {
        _notifications.Error(result.IsConnectionFailure ? "Connection unavailable: nothing was printed." : ApiFailures.Describe(result), result.CorrelationId);
        return null;
    }
}

public interface IKotAutoPrinter
{
    void Start();

    void Stop();
}

// Prints a kitchen ticket the moment it is created, on kitchen terminals that have a kitchen printer.
public sealed class KotAutoPrinter : IKotAutoPrinter
{
    private readonly IRealtimeClient _realtime;
    private readonly IClientSettingsService _settings;
    private readonly IDocumentPrinter _printer;
    private readonly ILogger<KotAutoPrinter> _logger;
    private IDisposable? _subscription;

    public KotAutoPrinter(IRealtimeClient realtime, IClientSettingsService settings, IDocumentPrinter printer, ILogger<KotAutoPrinter> logger)
    {
        _realtime = realtime;
        _settings = settings;
        _printer = printer;
        _logger = logger;
    }

    public void Start() => _subscription ??= _realtime.Subscribe<KitchenTicketCreatedEvent>(HubEvents.KitchenTicketCreated, OnTicketCreated);

    public void Stop()
    {
        _subscription?.Dispose();
        _subscription = null;
    }

    public bool ShouldPrint(int stationId)
    {
        var settings = _settings.Current;
        return settings.DeviceType == DeviceType.Kitchen
            && settings.AutoPrintKot
            && (settings.StationId is null || settings.StationId == stationId)
            && _printer.CanPrint(PrintDocumentType.Kot);
    }

    private void OnTicketCreated(KitchenTicketCreatedEvent e)
    {
        if (!ShouldPrint(e.StationId))
        {
            return;
        }

        // The event only announces the ticket; the document itself always comes from the server.
        _ = _printer.PrintAsync(PrintDocumentType.Kot, e.TicketId, quietWhenUnconfigured: true)
            .ContinueWith(t => _logger.LogError(t.Exception, "Auto-print of ticket {Ticket} failed", e.TicketNumber), TaskContinuationOptions.OnlyOnFaulted);
    }
}
