using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Floor;
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
    private const string FloorManagers = Roles.Admin + "," + Roles.Manager;

    private readonly ISectionService _sections;

    public SectionsController(ISectionService sections)
    {
        _sections = sections;
    }

    [HttpGet]
    [ProducesResponseType<ApiResponse<IReadOnlyList<SectionDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] bool includeInactive, CancellationToken cancellationToken)
    {
        if (includeInactive && !CanManageFloor())
        {
            return Failure(AppErrors.Forbidden("Only managers can list inactive sections."));
        }

        return Envelope(await _sections.ListAsync(includeInactive, cancellationToken));
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType<ApiResponse<SectionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken) =>
        FromResult(await _sections.GetAsync(id, cancellationToken));

    [HttpPost]
    [Authorize(Roles = FloorManagers)]
    [ProducesResponseType<ApiResponse<SectionDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateSectionRequest request, CancellationToken cancellationToken) =>
        FromResult(await _sections.CreateAsync(request, cancellationToken), StatusCodes.Status201Created, "Section created.");

    [HttpPut("{id:int}")]
    [Authorize(Roles = FloorManagers)]
    [ProducesResponseType<ApiResponse<SectionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(int id, UpdateSectionRequest request, CancellationToken cancellationToken) =>
        FromResult(await _sections.UpdateAsync(id, request, cancellationToken), message: "Section updated.");

    [HttpDelete("{id:int}")]
    [Authorize(Roles = FloorManagers)]
    [ProducesResponseType<ApiResponse<SectionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Deactivate(int id, CancellationToken cancellationToken) =>
        FromResult(await _sections.DeactivateAsync(id, cancellationToken), message: "Section deactivated.");

    private bool CanManageFloor() => User.IsInRole(Roles.Admin) || User.IsInRole(Roles.Manager);
}

[Route("api/tables")]
[Authorize]
public sealed class TablesController : ApiControllerBase
{
    private const string FloorManagers = Roles.Admin + "," + Roles.Manager;

    // Admin is included: it holds every permission, and the Tables module is open to it.
    private const string FloorOperators = Roles.Admin + "," + Roles.Manager + "," + Roles.Waiter + "," + Roles.Cashier;

    private readonly ITableService _tables;

    public TablesController(ITableService tables)
    {
        _tables = tables;
    }

    [HttpGet]
    [ProducesResponseType<ApiResponse<TableMapDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Map([FromQuery] bool includeInactive, CancellationToken cancellationToken)
    {
        if (includeInactive && !(User.IsInRole(Roles.Admin) || User.IsInRole(Roles.Manager)))
        {
            return Failure(AppErrors.Forbidden("Only managers can list inactive tables."));
        }

        return Envelope(await _tables.GetMapAsync(includeInactive, cancellationToken));
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType<ApiResponse<TableDetailDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken) =>
        FromResult(await _tables.GetAsync(id, cancellationToken));

    [HttpPost]
    [Authorize(Roles = FloorManagers)]
    [ProducesResponseType<ApiResponse<TableDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateTableRequest request, CancellationToken cancellationToken) =>
        FromResult(await _tables.CreateAsync(request, cancellationToken), StatusCodes.Status201Created, "Table created.");

    [HttpPut("{id:int}")]
    [Authorize(Roles = FloorManagers)]
    [ProducesResponseType<ApiResponse<TableDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(int id, UpdateTableRequest request, CancellationToken cancellationToken) =>
        FromResult(await _tables.UpdateAsync(id, request, cancellationToken), message: "Table updated.");

    [HttpDelete("{id:int}")]
    [Authorize(Roles = FloorManagers)]
    [ProducesResponseType<ApiResponse<TableDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Deactivate(int id, CancellationToken cancellationToken) =>
        FromResult(await _tables.DeactivateAsync(id, cancellationToken), message: "Table deactivated.");

    [HttpPost("{id:int}/occupy")]
    [Authorize(Roles = FloorOperators)]
    [ProducesResponseType<ApiResponse<TableDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Occupy(int id, OccupyTableRequest request, CancellationToken cancellationToken) =>
        FromResult(await _tables.OccupyAsync(id, request, cancellationToken), message: "Table occupied.");

    [HttpPost("{id:int}/release")]
    [Authorize(Roles = FloorOperators)]
    [ProducesResponseType<ApiResponse<TableDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Release(int id, CancellationToken cancellationToken) =>
        FromResult(await _tables.ReleaseAsync(id, cancellationToken), message: "Table released.");

    [HttpPost("{id:int}/out-of-service")]
    [Authorize(Roles = FloorManagers)]
    [ProducesResponseType<ApiResponse<TableDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> OutOfService(int id, CancellationToken cancellationToken) =>
        FromResult(await _tables.SetServiceStateAsync(id, outOfService: true, cancellationToken), message: "Table set out of service.");

    [HttpPost("{id:int}/in-service")]
    [Authorize(Roles = FloorManagers)]
    [ProducesResponseType<ApiResponse<TableDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> InService(int id, CancellationToken cancellationToken) =>
        FromResult(await _tables.SetServiceStateAsync(id, outOfService: false, cancellationToken), message: "Table back in service.");
}
