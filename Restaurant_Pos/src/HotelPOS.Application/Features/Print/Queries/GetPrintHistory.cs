using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Print;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Print.Queries;

// Every copy of a document, original first: who printed it, where, and whether it came out.
public sealed record GetPrintHistoryQuery(PrintDocumentType DocumentType, int DocumentId) : IQuery<IReadOnlyList<PrintLogDto>>, IRequirePermission
{
    public string Permission => Permissions.BillingOperate;
}

internal sealed class GetPrintHistoryHandler : IQueryHandler<GetPrintHistoryQuery, IReadOnlyList<PrintLogDto>>
{
    private readonly IAppDbContext _db;
    private readonly PrintLog _log;

    public GetPrintHistoryHandler(IAppDbContext db, PrintLog log)
    {
        _db = db;
        _log = log;
    }

    public async Task<Result<IReadOnlyList<PrintLogDto>>> Handle(GetPrintHistoryQuery request, CancellationToken cancellationToken)
    {
        var entries = await _db.PrintLogs.AsNoTracking()
            .Where(p => p.DocumentType == request.DocumentType && p.DocumentId == request.DocumentId)
            .OrderBy(p => p.CopyNumber)
            .ToListAsync(cancellationToken);
        return Result<IReadOnlyList<PrintLogDto>>.Ok(await _log.ToDtosAsync(entries, cancellationToken));
    }
}
