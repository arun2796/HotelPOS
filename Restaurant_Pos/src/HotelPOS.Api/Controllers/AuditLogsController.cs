using HotelPOS.Application.Administration;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelPOS.Api.Controllers;

[Route("api/admin/audit-logs")]
[Authorize(Policy = Permissions.AuditView)]
public sealed class AuditLogsController : ApiControllerBase
{
    private readonly IAuditQueryService _auditQuery;

    public AuditLogsController(IAuditQueryService auditQuery)
    {
        _auditQuery = auditQuery;
    }

    [HttpGet]
    [ProducesResponseType<ApiResponse<PagedResult<AuditLogDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] AuditLogQuery query, CancellationToken cancellationToken) =>
        Envelope(await _auditQuery.QueryAsync(query, cancellationToken));

    [HttpGet("export")]
    [Produces("text/csv")]
    public async Task<IActionResult> Export([FromQuery] AuditLogQuery query, CancellationToken cancellationToken)
    {
        var bytes = await _auditQuery.ExportCsvAsync(query, cancellationToken);
        var filename = $"audit-logs-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv";
        return File(bytes, "text/csv; charset=utf-8", filename);
    }
}

