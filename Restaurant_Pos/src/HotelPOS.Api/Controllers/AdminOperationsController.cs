using HotelPOS.Api.Common;
using HotelPOS.Application.Administration;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelPOS.Api.Controllers;

[Route("api/admin")]
[Authorize(Policy = Permissions.ServerManage)]
public sealed class AdminOperationsController : ApiControllerBase
{
    private readonly LogTailReader _logs;
    private readonly IServerNoticeService _notices;

    public AdminOperationsController(LogTailReader logs, IServerNoticeService notices)
    {
        _logs = logs;
        _notices = notices;
    }

    [HttpGet("logs")]
    [ProducesResponseType<ApiResponse<LogTailDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Logs([FromQuery] LogTailQuery query, CancellationToken cancellationToken) =>
        Envelope(await _logs.ReadAsync(query, cancellationToken) ?? new LogTailDto());

    [HttpPost("notice")]
    public async Task<IActionResult> Notice(ServerNoticeRequest request, CancellationToken cancellationToken)
    {
        await _notices.SendAsync(request, cancellationToken);
        return Envelope<object?>(null, message: "Notice sent to every connected terminal.");
    }
}
