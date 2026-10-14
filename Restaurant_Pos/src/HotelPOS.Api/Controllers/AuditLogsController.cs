using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Features.Operations.Queries;
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
    [HttpGet]
    [ProducesResponseType<ApiResponse<PagedResult<AuditLogDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] AuditLogQuery query, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new ListAuditLogsQuery(query), cancellationToken));

    // CSV by default; format=xlsx (Phase 14) gives an Excel workbook with typed cells, format=pdf a PDF on the letterhead.
    [HttpGet("export")]
    [Produces("text/csv", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "application/pdf")]
    public async Task<IActionResult> Export([FromQuery] AuditLogQuery query, [FromQuery] string? format, CancellationToken cancellationToken)
    {
        var kind = ExportFormats.Parse(format);
        kind = kind == ExportFormat.Json ? ExportFormat.Csv : kind;
        var result = await Sender.Send(new ExportAuditLogsQuery(query, kind), cancellationToken);
        if (result.IsFailure)
        {
            return Failure(result.Error!);
        }

        var filename = $"audit-logs-{DateTime.UtcNow:yyyyMMdd-HHmmss}{ExportFormats.Extension(kind)}";
        return File(result.Value, ExportFormats.ContentType(kind), filename);
    }
}
