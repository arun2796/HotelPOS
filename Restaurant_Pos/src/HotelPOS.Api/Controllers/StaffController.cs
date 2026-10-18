using HotelPOS.Application.Features.Reports.Queries;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Reports;
using HotelPOS.Contracts.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelPOS.Api.Controllers;

[Route("api/staff")]
[Authorize]
public sealed class StaffController : ApiControllerBase
{
    // Live connections + open shifts + active orders; presence, not attendance.
    [HttpGet("on-duty")]
    [Authorize(Policy = Permissions.ReportsView)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<StaffOnDutyDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> OnDuty(CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetStaffOnDutyQuery(), cancellationToken));
}
