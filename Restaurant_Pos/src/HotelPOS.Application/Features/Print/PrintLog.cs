using System.Globalization;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Print;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Printing;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Print;

public sealed record ReservedCopy(object Document, PrintLogEntry Entry);

// The print log: every printed or PDF copy of a document gets the next copy number (1 = original, later copies say
// DUPLICATE), is audited as Print.Copy and announced to managers after the commit.
internal sealed class PrintLog
{
    private readonly IAppDbContext _db;
    private readonly PrintDocuments _documents;
    private readonly IPrintCopyAllocator _allocator;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IAuditTrail _audit;
    private readonly IAfterCommit _afterCommit;
    private readonly IRealtimeNotifier _realtime;

    public PrintLog(
        IAppDbContext db,
        PrintDocuments documents,
        IPrintCopyAllocator allocator,
        ICurrentUser currentUser,
        IClock clock,
        IAuditTrail audit,
        IAfterCommit afterCommit,
        IRealtimeNotifier realtime)
    {
        _db = db;
        _documents = documents;
        _allocator = allocator;
        _currentUser = currentUser;
        _clock = clock;
        _audit = audit;
        _afterCommit = afterCommit;
        _realtime = realtime;
    }

    public static string EntityType(PrintDocumentType type) => type switch
    {
        PrintDocumentType.Invoice => "Bill",
        PrintDocumentType.Receipt => "Payment",
        _ => "KitchenTicket",
    };

    public async Task<Result<ReservedCopy>> ReserveAsync(
        PrintDocumentType type,
        int documentId,
        PrintOutput output,
        string? printerName,
        Guid? idempotencyKey,
        CancellationToken cancellationToken)
    {
        // The document is built first, so a missing bill or another waiter's invoice never takes a copy number.
        var built = await _documents.BuildAsync(type, documentId, cancellationToken);
        if (built.IsFailure)
        {
            return built.Error!;
        }

        var copy = await _allocator.NextCopyNumberAsync(type, documentId, cancellationToken);
        var entry = new PrintLogEntry(type, documentId, copy, output, printerName, _currentUser.DeviceId, _currentUser.UserId, _clock.UtcNow, idempotencyKey);
        _db.PrintLogs.Add(entry);
        await _db.SaveChangesAsync(cancellationToken);

        _audit.Add(AuditActions.PrintedCopy, EntityType(type), documentId.ToString(CultureInfo.InvariantCulture), newValues: new
        {
            DocumentType = type,
            entry.CopyNumber,
            entry.CopyLabel,
            Output = output,
            Printer = entry.PrinterName,
            Device = _currentUser.DeviceName,
        });
        Announce(entry);
        return new ReservedCopy(PrintDocuments.WithCopy(built.Value, copy, entry.Id), entry);
    }

    public void Announce(PrintLogEntry entry)
    {
        var printed = new DocumentPrintedEvent
        {
            OccurredAtUtc = _clock.UtcNow,
            EntityId = entry.DocumentId,
            DocumentType = entry.DocumentType,
            DocumentId = entry.DocumentId,
            CopyNumber = entry.CopyNumber,
            Output = entry.Output,
            Result = entry.Result,
        };
        _afterCommit.Enqueue(() => _realtime.PublishAsync(HubEvents.DocumentPrinted, printed, RealtimeAudience.ForRoles(Roles.Admin, Roles.Manager)));
    }

    // The terminal reported a reserved copy failed: tell that terminal (or its user) and the managers, after the commit.
    public void AnnounceFailure(PrintLogEntry entry, string? documentNumber)
    {
        var failed = new PrintFailedEvent
        {
            OccurredAtUtc = _clock.UtcNow,
            EntityId = entry.DocumentId,
            DocumentType = entry.DocumentType,
            DocumentId = entry.DocumentId,
            CopyNumber = entry.CopyNumber,
            Output = entry.Output,
            Error = entry.Error,
            DeviceName = _currentUser.DeviceName,
            DocumentNumber = documentNumber,
        };
        var managers = new[] { Roles.Admin, Roles.Manager };
        var audience = _currentUser.DeviceId is { } deviceId
            ? new RealtimeAudience { DeviceIds = new[] { deviceId }, Roles = managers }
            : new RealtimeAudience { UserIds = _currentUser.UserId is { } userId ? new[] { userId } : Array.Empty<int>(), Roles = managers };
        _afterCommit.Enqueue(() => _realtime.PublishAsync(HubEvents.PrintFailed, failed, audience));
    }

    // The number a cashier knows the document by: the invoice number for an invoice or a receipt, the ticket number
    // for a KOT. Null when the document has none (yet) or no longer exists.
    public async Task<string?> DocumentNumberAsync(PrintDocumentType type, int documentId, CancellationToken cancellationToken) =>
        type switch
        {
            PrintDocumentType.Invoice => await _db.Bills.AsNoTracking().Where(b => b.Id == documentId)
                .Select(b => b.InvoiceNumber).FirstOrDefaultAsync(cancellationToken),
            PrintDocumentType.Receipt => await _db.Payments.AsNoTracking().Where(p => p.Id == documentId)
                .Join(_db.Bills.AsNoTracking(), p => p.BillId, b => b.Id, (p, b) => b.InvoiceNumber).FirstOrDefaultAsync(cancellationToken),
            PrintDocumentType.Kot => await _db.KitchenOrders.AsNoTracking().Where(k => k.Id == documentId)
                .Select(k => k.TicketNumber).FirstOrDefaultAsync(cancellationToken),
            _ => null,
        };

    public async Task<IReadOnlyList<PrintLogDto>> ToDtosAsync(IReadOnlyList<PrintLogEntry> entries, CancellationToken cancellationToken)
    {
        var userIds = entries.Select(e => e.UserId).OfType<int>().Distinct().ToList();
        var deviceIds = entries.Select(e => e.DeviceId).OfType<Guid>().Distinct().ToList();
        var users = await _db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);
        var devices = await _db.Devices.AsNoTracking().Where(d => deviceIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Name, cancellationToken);
        return entries.Select(e => new PrintLogDto
        {
            Id = e.Id,
            DocumentType = e.DocumentType,
            DocumentId = e.DocumentId,
            CopyNumber = e.CopyNumber,
            CopyLabel = e.CopyLabel,
            Output = e.Output,
            PrinterName = e.PrinterName,
            DeviceName = e.DeviceId is { } d ? devices.GetValueOrDefault(d) : null,
            UserName = e.UserId is { } u ? users.GetValueOrDefault(u) : null,
            CreatedAtUtc = e.CreatedAt,
            Result = e.Result,
            Error = e.Error,
        }).ToList();
    }
}
