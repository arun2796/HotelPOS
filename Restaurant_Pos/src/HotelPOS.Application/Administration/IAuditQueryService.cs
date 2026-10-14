using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Common;

namespace HotelPOS.Application.Administration;

public interface IAuditQueryService
{
    Task<PagedResult<AuditLogDto>> QueryAsync(AuditLogQuery query, CancellationToken cancellationToken = default);

    Task<byte[]> ExportCsvAsync(AuditLogQuery query, CancellationToken cancellationToken = default);
}

