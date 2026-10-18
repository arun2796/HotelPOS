using HotelPOS.Application.Features.Floor.Commands;
using HotelPOS.Application.Features.Floor.Queries;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelPOS.Api.Controllers;

[Route("api/sections")]
[Authorize]
public sealed class FloorLayoutController : ApiControllerBase
{
    [HttpGet("{id:int}/layout")]
    [Authorize(Policy = Permissions.TablesView)]
    [ProducesResponseType<ApiResponse<FloorLayoutDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetFloorLayoutQuery(id), cancellationToken));

    [HttpPut("{id:int}/layout")]
    [Authorize(Policy = Permissions.FloorLayoutManage)]
    [ProducesResponseType<ApiResponse<FloorLayoutDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Save(int id, SaveFloorLayoutRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new SaveFloorLayoutCommand(id, request), cancellationToken), message: "Floor layout saved.");
}
