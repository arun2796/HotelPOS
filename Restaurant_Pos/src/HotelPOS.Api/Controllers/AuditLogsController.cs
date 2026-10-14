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

    [HttpGet("export")]
    [Produces("text/csv")]
    public async Task<IActionResult> Export([FromQuery] AuditLogQuery query, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new ExportAuditLogsQuery(query), cancellationToken);
        if (result.IsFailure)
        {
            return Failure(result.Error!);
        }

        var filename = $"audit-logs-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv";
        return File(result.Value, "text/csv; charset=utf-8", filename);
    }
}
