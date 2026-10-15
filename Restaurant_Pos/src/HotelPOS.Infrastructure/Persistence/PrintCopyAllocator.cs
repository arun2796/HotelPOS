using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Contracts.Print;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Infrastructure.Persistence;

public sealed class PrintCopyAllocator : IPrintCopyAllocator
{
    // Namespace of the advisory locks (first key); the second key is the document. Only print copies use it.
    private const int LockNamespace = 14_0001;

    private readonly AppDbContext _db;

    public PrintCopyAllocator(AppDbContext db)
    {
        _db = db;
    }

    public async Task<int> NextCopyNumberAsync(PrintDocumentType documentType, int documentId, CancellationToken cancellationToken = default)
    {
        if (_db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Copy numbers are allocated only inside the printing command's transaction.");
        }

        // The transaction-scoped lock serialises reservations of the same document until commit: a second terminal
        // waits, then reads the first one's committed copy and takes the next number (2 and 3, never two 1s). The
        // unique index on (DocumentType, DocumentId, CopyNumber) backs the rule up.
        var key = (LockNamespace * 10) + (int)documentType;
        await _db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({key}, {documentId})", cancellationToken);
        var last = await _db.Database.SqlQuery<int>($"""
            SELECT COALESCE(MAX("CopyNumber"), 0) AS "Value" FROM "PrintLogs"
            WHERE "DocumentType" = {(int)documentType} AND "DocumentId" = {documentId}
            """).SingleAsync(cancellationToken);
        return last + 1;
    }
}
