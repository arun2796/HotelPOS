using HotelPOS.Application.Features.Menu.Commands;
using HotelPOS.Application.Features.Menu.Queries;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelPOS.Api.Controllers;

[Route("api/menu")]
[Authorize]
public sealed class MenuController : ApiControllerBase
{
    [HttpGet]
    [Authorize(Policy = Permissions.MenuView)]
    [ProducesResponseType<ApiResponse<MenuDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get([FromQuery] int? version, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetMenuQuery(version), cancellationToken));
}

[Route("api/categories")]
[Authorize]
public sealed class CategoriesController : ApiControllerBase
{
    [HttpGet]
    [Authorize(Policy = Permissions.MenuView)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<CategoryDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] bool includeInactive, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new ListCategoriesQuery(includeInactive), cancellationToken));

    [HttpGet("{id:int}")]
    [Authorize(Policy = Permissions.MenuView)]
    [ProducesResponseType<ApiResponse<CategoryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetCategoryQuery(id), cancellationToken));

    [HttpPost]
    [Authorize(Policy = Permissions.MenuManage)]
    [ProducesResponseType<ApiResponse<CategoryDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateCategoryRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new CreateCategoryCommand(request), cancellationToken), StatusCodes.Status201Created, "Category created.");

    [HttpPut("{id:int}")]
    [Authorize(Policy = Permissions.MenuManage)]
    [ProducesResponseType<ApiResponse<CategoryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(int id, UpdateCategoryRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new UpdateCategoryCommand(id, request), cancellationToken), message: "Category updated.");

    [HttpDelete("{id:int}")]
    [Authorize(Policy = Permissions.MenuManage)]
    [ProducesResponseType<ApiResponse<CategoryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Deactivate(int id, [FromQuery] bool deactivateItems, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new DeactivateCategoryCommand(id, deactivateItems), cancellationToken), message: "Category deactivated.");
}

[Route("api/menu-items")]
[Authorize]
public sealed class MenuItemsController : ApiControllerBase
{
    private const long MaxUploadRequestBytes = 10 * 1024 * 1024;

    [HttpGet]
    [Authorize(Policy = Permissions.MenuView)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<MenuItemDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] MenuItemQuery query, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new ListMenuItemsQuery(query), cancellationToken));

    [HttpGet("{id:int}")]
    [Authorize(Policy = Permissions.MenuView)]
    [ProducesResponseType<ApiResponse<MenuItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetMenuItemQuery(id), cancellationToken));

    [HttpPost]
    [Authorize(Policy = Permissions.MenuManage)]
    [ProducesResponseType<ApiResponse<MenuItemDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateMenuItemRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new CreateMenuItemCommand(request), cancellationToken), StatusCodes.Status201Created, "Item created.");

    [HttpPut("{id:int}")]
    [Authorize(Policy = Permissions.MenuManage)]
    [ProducesResponseType<ApiResponse<MenuItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(int id, UpdateMenuItemRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new UpdateMenuItemCommand(id, request), cancellationToken), message: "Item updated.");

    [HttpDelete("{id:int}")]
    [Authorize(Policy = Permissions.MenuManage)]
    [ProducesResponseType<ApiResponse<MenuItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Deactivate(int id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new DeactivateMenuItemCommand(id), cancellationToken), message: "Item deactivated.");

    [HttpPatch("{id:int}/availability")]
    [Authorize(Policy = Permissions.MenuAvailability)]
    [ProducesResponseType<ApiResponse<MenuItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetAvailability(int id, SetAvailabilityRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new SetMenuItemAvailabilityCommand(id, request), cancellationToken),
            message: request.IsAvailable ? "Item available again." : "Item marked sold out.");

    [HttpPost("{id:int}/image")]
    [Authorize(Policy = Permissions.MenuManage)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxUploadRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadRequestBytes)]
    [ProducesResponseType<ApiResponse<MenuItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UploadImage(int id, IFormFile? file, CancellationToken cancellationToken)
    {
        await using var stream = file?.OpenReadStream();
        return FromResult(await Sender.Send(new UploadMenuItemImageCommand(id, stream, file?.Length ?? 0), cancellationToken), message: "Picture saved.");
    }

    [HttpDelete("{id:int}/image")]
    [Authorize(Policy = Permissions.MenuManage)]
    [ProducesResponseType<ApiResponse<MenuItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RemoveImage(int id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new RemoveMenuItemImageCommand(id), cancellationToken), message: "Picture removed.");
}

[Route("api/modifier-groups")]
[Authorize]
public sealed class ModifierGroupsController : ApiControllerBase
{
    [HttpGet]
    [Authorize(Policy = Permissions.MenuView)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<ModifierGroupDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] bool includeInactive, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new ListModifierGroupsQuery(includeInactive), cancellationToken));

    [HttpGet("{id:int}")]
    [Authorize(Policy = Permissions.MenuView)]
    [ProducesResponseType<ApiResponse<ModifierGroupDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetModifierGroupQuery(id), cancellationToken));

    [HttpPost]
    [Authorize(Policy = Permissions.MenuManage)]
    [ProducesResponseType<ApiResponse<ModifierGroupDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(SaveModifierGroupRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new CreateModifierGroupCommand(request), cancellationToken), StatusCodes.Status201Created, "Modifier group created.");

    [HttpPut("{id:int}")]
    [Authorize(Policy = Permissions.MenuManage)]
    [ProducesResponseType<ApiResponse<ModifierGroupDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(int id, SaveModifierGroupRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new UpdateModifierGroupCommand(id, request), cancellationToken), message: "Modifier group updated.");

    [HttpDelete("{id:int}")]
    [Authorize(Policy = Permissions.MenuManage)]
    [ProducesResponseType<ApiResponse<ModifierGroupDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Deactivate(int id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new DeactivateModifierGroupCommand(id), cancellationToken), message: "Modifier group deactivated.");

    [HttpPost("{id:int}/options")]
    [Authorize(Policy = Permissions.MenuManage)]
    [ProducesResponseType<ApiResponse<ModifierGroupDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> AddOption(int id, SaveModifierOptionRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new AddModifierOptionCommand(id, request), cancellationToken), StatusCodes.Status201Created, "Option added.");

    [HttpPut("{id:int}/options/{optionId:int}")]
    [Authorize(Policy = Permissions.MenuManage)]
    [ProducesResponseType<ApiResponse<ModifierGroupDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateOption(int id, int optionId, SaveModifierOptionRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new UpdateModifierOptionCommand(id, optionId, request), cancellationToken), message: "Option updated.");

    [HttpDelete("{id:int}/options/{optionId:int}")]
    [Authorize(Policy = Permissions.MenuManage)]
    [ProducesResponseType<ApiResponse<ModifierGroupDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> DeactivateOption(int id, int optionId, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new DeactivateModifierOptionCommand(id, optionId), cancellationToken), message: "Option deactivated.");
}

[Route("api/taxes")]
[Authorize]
public sealed class TaxesController : ApiControllerBase
{
    [HttpGet]
    [Authorize(Policy = Permissions.MenuView)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<TaxDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] bool includeInactive, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new ListTaxesQuery(includeInactive), cancellationToken));

    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<ApiResponse<TaxDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(SaveTaxRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new CreateTaxCommand(request), cancellationToken), StatusCodes.Status201Created, "Tax created.");

    [HttpPut("{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<ApiResponse<TaxDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(int id, SaveTaxRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new UpdateTaxCommand(id, request), cancellationToken), message: "Tax updated.");

    [HttpDelete("{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<ApiResponse<TaxDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Deactivate(int id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new DeactivateTaxCommand(id), cancellationToken), message: "Tax deactivated.");
}

[Route("api/stations")]
[Authorize]
public sealed class StationsController : ApiControllerBase
{
    [HttpGet]
    [Authorize(Policy = Permissions.MenuView)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<StationDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] bool includeInactive, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new ListStationsQuery(includeInactive), cancellationToken));

    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<ApiResponse<StationDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(SaveStationRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new CreateStationCommand(request), cancellationToken), StatusCodes.Status201Created, "Station created.");

    [HttpPut("{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<ApiResponse<StationDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(int id, SaveStationRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new UpdateStationCommand(id, request), cancellationToken), message: "Station updated.");

    [HttpDelete("{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<ApiResponse<StationDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Deactivate(int id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new DeactivateStationCommand(id), cancellationToken), message: "Station deactivated.");
}
