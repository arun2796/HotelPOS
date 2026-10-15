using HotelPOS.Application.Features.Operations.Commands;
using HotelPOS.Application.Features.Operations.Queries;
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
    [HttpGet("logs")]
    [ProducesResponseType<ApiResponse<LogTailDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Logs([FromQuery] LogTailQuery query, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetLogTailQuery(query), cancellationToken));

    [HttpPost("notice")]
    public async Task<IActionResult> Notice(ServerNoticeRequest request, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new SendServerNoticeCommand(request), cancellationToken);
        return result.IsSuccess
            ? Envelope<object?>(null, message: "Notice sent to every connected terminal.")
            : Failure(result.Error!);
    }
}
