using HotelPOS.Application.Features.Denominations.Commands;
using HotelPOS.Application.Features.Denominations.Queries;
using HotelPOS.Contracts.Cash;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelPOS.Api.Controllers;

[Route("api/denominations")]
[Authorize]
public sealed class DenominationsController : ApiControllerBase
{
    [HttpGet]
    [Authorize(Policy = Permissions.BillingOperate)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<DenominationDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new ListDenominationsQuery(), cancellationToken));

    [HttpPut]
    [Authorize(Policy = Permissions.SettingsManage)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<DenominationDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Save(SaveDenominationsRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new SaveDenominationsCommand(request), cancellationToken), message: "Denominations saved.");
}
