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
public sealed class SectionsController : ApiControllerBase
{
    [HttpGet]
    [Authorize(Policy = Permissions.TablesView)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<SectionDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] bool includeInactive, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new ListSectionsQuery(includeInactive), cancellationToken));

    [HttpGet("{id:int}")]
    [Authorize(Policy = Permissions.TablesView)]
    [ProducesResponseType<ApiResponse<SectionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetSectionQuery(id), cancellationToken));

    [HttpPost]
    [Authorize(Policy = Permissions.TablesManage)]
    [ProducesResponseType<ApiResponse<SectionDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateSectionRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new CreateSectionCommand(request), cancellationToken), StatusCodes.Status201Created, "Section created.");

    [HttpPut("{id:int}")]
    [Authorize(Policy = Permissions.TablesManage)]
    [ProducesResponseType<ApiResponse<SectionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(int id, UpdateSectionRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new UpdateSectionCommand(id, request), cancellationToken), message: "Section updated.");

    [HttpDelete("{id:int}")]
    [Authorize(Policy = Permissions.TablesManage)]
    [ProducesResponseType<ApiResponse<SectionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Deactivate(int id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new DeactivateSectionCommand(id), cancellationToken), message: "Section deactivated.");
}

[Route("api/tables")]
[Authorize]
public sealed class TablesController : ApiControllerBase
{
    [HttpGet]
    [Authorize(Policy = Permissions.TablesView)]
    [ProducesResponseType<ApiResponse<TableMapDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Map([FromQuery] bool includeInactive, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetTableMapQuery(includeInactive), cancellationToken));

    [HttpGet("{id:int}")]
    [Authorize(Policy = Permissions.TablesView)]
    [ProducesResponseType<ApiResponse<TableDetailDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetTableQuery(id), cancellationToken));

    [HttpPost]
    [Authorize(Policy = Permissions.TablesManage)]
    [ProducesResponseType<ApiResponse<TableDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateTableRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new CreateTableCommand(request), cancellationToken), StatusCodes.Status201Created, "Table created.");

    [HttpPut("{id:int}")]
    [Authorize(Policy = Permissions.TablesManage)]
    [ProducesResponseType<ApiResponse<TableDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(int id, UpdateTableRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new UpdateTableCommand(id, request), cancellationToken), message: "Table updated.");

    [HttpDelete("{id:int}")]
    [Authorize(Policy = Permissions.TablesManage)]
    [ProducesResponseType<ApiResponse<TableDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Deactivate(int id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new DeactivateTableCommand(id), cancellationToken), message: "Table deactivated.");

    [HttpPost("{id:int}/occupy")]
    [Authorize(Policy = Permissions.TablesOperate)]
    [ProducesResponseType<ApiResponse<TableDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Occupy(int id, OccupyTableRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new OccupyTableCommand(id, request), cancellationToken), message: "Table occupied.");

    [HttpPost("{id:int}/release")]
    [Authorize(Policy = Permissions.TablesOperate)]
    [ProducesResponseType<ApiResponse<TableDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Release(int id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new ReleaseTableCommand(id), cancellationToken), message: "Table released.");

    [HttpPost("{id:int}/out-of-service")]
    [Authorize(Policy = Permissions.TablesManage)]
    [ProducesResponseType<ApiResponse<TableDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> OutOfService(int id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new SetTableServiceStateCommand(id, OutOfService: true), cancellationToken), message: "Table set out of service.");

    [HttpPost("{id:int}/in-service")]
    [Authorize(Policy = Permissions.TablesManage)]
    [ProducesResponseType<ApiResponse<TableDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> InService(int id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new SetTableServiceStateCommand(id, OutOfService: false), cancellationToken), message: "Table back in service.");
}
