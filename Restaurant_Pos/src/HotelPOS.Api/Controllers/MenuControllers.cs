using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Menu;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelPOS.Api.Controllers;

/// <summary>Role groups for the menu endpoints (docs/03-api-reference.md § 3.4).</summary>
internal static class MenuRoles
{
    public const string Managers = Roles.Admin + "," + Roles.Manager;
    public const string Availability = Roles.Admin + "," + Roles.Manager + "," + Roles.Kitchen;
}

/// <summary>Compact active menu for ordering terminals.</summary>
[Route("api/menu")]
[Authorize]
public sealed class MenuController : ApiControllerBase
{
    private readonly IMenuQuery _menu;

    public MenuController(IMenuQuery menu)
    {
        _menu = menu;
    }

    /// <summary>The active menu. With <c>?version=N</c> equal to the current version, only <c>notModified: true</c>.</summary>
    [HttpGet]
    [ProducesResponseType<ApiResponse<MenuDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get([FromQuery] int? version, CancellationToken cancellationToken) =>
        Envelope(await _menu.GetMenuAsync(version, cancellationToken));
}

[Route("api/categories")]
[Authorize]
public sealed class CategoriesController : ApiControllerBase
{
    private readonly ICategoryService _categories;

    public CategoriesController(ICategoryService categories)
    {
        _categories = categories;
    }

    [HttpGet]
    [ProducesResponseType<ApiResponse<IReadOnlyList<CategoryDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] bool includeInactive, CancellationToken cancellationToken) =>
        includeInactive && !IsManager()
            ? Failure(AppErrors.Forbidden("Only managers can list inactive categories."))
            : Envelope(await _categories.ListAsync(includeInactive, cancellationToken));

    [HttpGet("{id:int}")]
    [ProducesResponseType<ApiResponse<CategoryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken) =>
        FromResult(await _categories.GetAsync(id, cancellationToken));

    [HttpPost]
    [Authorize(Roles = MenuRoles.Managers)]
    [ProducesResponseType<ApiResponse<CategoryDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateCategoryRequest request, CancellationToken cancellationToken) =>
        FromResult(await _categories.CreateAsync(request, cancellationToken), StatusCodes.Status201Created, "Category created.");

    [HttpPut("{id:int}")]
    [Authorize(Roles = MenuRoles.Managers)]
    [ProducesResponseType<ApiResponse<CategoryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(int id, UpdateCategoryRequest request, CancellationToken cancellationToken) =>
        FromResult(await _categories.UpdateAsync(id, request, cancellationToken), message: "Category updated.");

    /// <summary>Deactivates the category; with <c>deactivateItems=true</c> its active items too.</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = MenuRoles.Managers)]
    [ProducesResponseType<ApiResponse<CategoryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Deactivate(int id, [FromQuery] bool deactivateItems, CancellationToken cancellationToken) =>
        FromResult(await _categories.DeactivateAsync(id, deactivateItems, cancellationToken), message: "Category deactivated.");

    private bool IsManager() => User.IsInRole(Roles.Admin) || User.IsInRole(Roles.Manager);
}

[Route("api/menu-items")]
[Authorize]
public sealed class MenuItemsController : ApiControllerBase
{
    private const long MaxUploadRequestBytes = 10 * 1024 * 1024;

    private readonly IMenuItemService _items;

    public MenuItemsController(IMenuItemService items)
    {
        _items = items;
    }

    [HttpGet]
    [ProducesResponseType<ApiResponse<IReadOnlyList<MenuItemDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] MenuItemQuery query, CancellationToken cancellationToken) =>
        query.IncludeInactive && !(User.IsInRole(Roles.Admin) || User.IsInRole(Roles.Manager))
            ? Failure(AppErrors.Forbidden("Only managers can list inactive items."))
            : Envelope(await _items.ListAsync(query, cancellationToken));

    [HttpGet("{id:int}")]
    [ProducesResponseType<ApiResponse<MenuItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken) =>
        FromResult(await _items.GetAsync(id, cancellationToken));

    [HttpPost]
    [Authorize(Roles = MenuRoles.Managers)]
    [ProducesResponseType<ApiResponse<MenuItemDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateMenuItemRequest request, CancellationToken cancellationToken) =>
        FromResult(await _items.CreateAsync(request, cancellationToken), StatusCodes.Status201Created, "Item created.");

    [HttpPut("{id:int}")]
    [Authorize(Roles = MenuRoles.Managers)]
    [ProducesResponseType<ApiResponse<MenuItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(int id, UpdateMenuItemRequest request, CancellationToken cancellationToken) =>
        FromResult(await _items.UpdateAsync(id, request, cancellationToken), message: "Item updated.");

    [HttpDelete("{id:int}")]
    [Authorize(Roles = MenuRoles.Managers)]
    [ProducesResponseType<ApiResponse<MenuItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Deactivate(int id, CancellationToken cancellationToken) =>
        FromResult(await _items.DeactivateAsync(id, cancellationToken), message: "Item deactivated.");

    /// <summary>Sold-out toggle.</summary>
    [HttpPatch("{id:int}/availability")]
    [Authorize(Roles = MenuRoles.Availability)]
    [ProducesResponseType<ApiResponse<MenuItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetAvailability(int id, SetAvailabilityRequest request, CancellationToken cancellationToken) =>
        FromResult(await _items.SetAvailabilityAsync(id, request.IsAvailable, cancellationToken),
            message: request.IsAvailable ? "Item available again." : "Item marked sold out.");

    /// <summary>Uploads the item picture (multipart field "file"; JPEG or PNG up to 2 MB).</summary>
    [HttpPost("{id:int}/image")]
    [Authorize(Roles = MenuRoles.Managers)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxUploadRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadRequestBytes)]
    [ProducesResponseType<ApiResponse<MenuItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UploadImage(int id, IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return Failure(AppErrors.Validation("file", "Choose a JPEG or PNG picture to upload."));
        }

        await using var stream = file.OpenReadStream();
        return FromResult(await _items.UploadImageAsync(id, stream, file.Length, cancellationToken), message: "Picture saved.");
    }

    [HttpDelete("{id:int}/image")]
    [Authorize(Roles = MenuRoles.Managers)]
    [ProducesResponseType<ApiResponse<MenuItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RemoveImage(int id, CancellationToken cancellationToken) =>
        FromResult(await _items.RemoveImageAsync(id, cancellationToken), message: "Picture removed.");
}

[Route("api/modifier-groups")]
[Authorize]
public sealed class ModifierGroupsController : ApiControllerBase
{
    private readonly IModifierService _modifiers;

    public ModifierGroupsController(IModifierService modifiers)
    {
        _modifiers = modifiers;
    }

    [HttpGet]
    [ProducesResponseType<ApiResponse<IReadOnlyList<ModifierGroupDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] bool includeInactive, CancellationToken cancellationToken) =>
        includeInactive && !(User.IsInRole(Roles.Admin) || User.IsInRole(Roles.Manager))
            ? Failure(AppErrors.Forbidden("Only managers can list inactive modifier groups."))
            : Envelope(await _modifiers.ListAsync(includeInactive, cancellationToken));

    [HttpGet("{id:int}")]
    [ProducesResponseType<ApiResponse<ModifierGroupDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken) =>
        FromResult(await _modifiers.GetAsync(id, cancellationToken));

    [HttpPost]
    [Authorize(Roles = MenuRoles.Managers)]
    [ProducesResponseType<ApiResponse<ModifierGroupDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(SaveModifierGroupRequest request, CancellationToken cancellationToken) =>
        FromResult(await _modifiers.CreateAsync(request, cancellationToken), StatusCodes.Status201Created, "Modifier group created.");

    [HttpPut("{id:int}")]
    [Authorize(Roles = MenuRoles.Managers)]
    [ProducesResponseType<ApiResponse<ModifierGroupDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(int id, SaveModifierGroupRequest request, CancellationToken cancellationToken) =>
        FromResult(await _modifiers.UpdateAsync(id, request, cancellationToken), message: "Modifier group updated.");

    [HttpDelete("{id:int}")]
    [Authorize(Roles = MenuRoles.Managers)]
    [ProducesResponseType<ApiResponse<ModifierGroupDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Deactivate(int id, CancellationToken cancellationToken) =>
        FromResult(await _modifiers.DeactivateAsync(id, cancellationToken), message: "Modifier group deactivated.");

    [HttpPost("{id:int}/options")]
    [Authorize(Roles = MenuRoles.Managers)]
    [ProducesResponseType<ApiResponse<ModifierGroupDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> AddOption(int id, SaveModifierOptionRequest request, CancellationToken cancellationToken) =>
        FromResult(await _modifiers.AddOptionAsync(id, request, cancellationToken), StatusCodes.Status201Created, "Option added.");

    [HttpPut("{id:int}/options/{optionId:int}")]
    [Authorize(Roles = MenuRoles.Managers)]
    [ProducesResponseType<ApiResponse<ModifierGroupDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateOption(int id, int optionId, SaveModifierOptionRequest request, CancellationToken cancellationToken) =>
        FromResult(await _modifiers.UpdateOptionAsync(id, optionId, request, cancellationToken), message: "Option updated.");

    [HttpDelete("{id:int}/options/{optionId:int}")]
    [Authorize(Roles = MenuRoles.Managers)]
    [ProducesResponseType<ApiResponse<ModifierGroupDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> DeactivateOption(int id, int optionId, CancellationToken cancellationToken) =>
        FromResult(await _modifiers.DeactivateOptionAsync(id, optionId, cancellationToken), message: "Option deactivated.");
}

/// <summary>Taxes: everyone reads, only Admin writes.</summary>
[Route("api/taxes")]
[Authorize]
public sealed class TaxesController : ApiControllerBase
{
    private readonly ITaxService _taxes;

    public TaxesController(ITaxService taxes)
    {
        _taxes = taxes;
    }

    [HttpGet]
    [ProducesResponseType<ApiResponse<IReadOnlyList<TaxDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] bool includeInactive, CancellationToken cancellationToken) =>
        Envelope(await _taxes.ListAsync(includeInactive, cancellationToken));

    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<ApiResponse<TaxDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(SaveTaxRequest request, CancellationToken cancellationToken) =>
        FromResult(await _taxes.CreateAsync(request, cancellationToken), StatusCodes.Status201Created, "Tax created.");

    [HttpPut("{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<ApiResponse<TaxDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(int id, SaveTaxRequest request, CancellationToken cancellationToken) =>
        FromResult(await _taxes.UpdateAsync(id, request, cancellationToken), message: "Tax updated.");

    [HttpDelete("{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<ApiResponse<TaxDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Deactivate(int id, CancellationToken cancellationToken) =>
        FromResult(await _taxes.DeactivateAsync(id, cancellationToken), message: "Tax deactivated.");
}

/// <summary>Preparation stations: everyone reads, only Admin writes.</summary>
[Route("api/stations")]
[Authorize]
public sealed class StationsController : ApiControllerBase
{
    private readonly IStationService _stations;

    public StationsController(IStationService stations)
    {
        _stations = stations;
    }

    [HttpGet]
    [ProducesResponseType<ApiResponse<IReadOnlyList<StationDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] bool includeInactive, CancellationToken cancellationToken) =>
        Envelope(await _stations.ListAsync(includeInactive, cancellationToken));

    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<ApiResponse<StationDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(SaveStationRequest request, CancellationToken cancellationToken) =>
        FromResult(await _stations.CreateAsync(request, cancellationToken), StatusCodes.Status201Created, "Station created.");

    [HttpPut("{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<ApiResponse<StationDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(int id, SaveStationRequest request, CancellationToken cancellationToken) =>
        FromResult(await _stations.UpdateAsync(id, request, cancellationToken), message: "Station updated.");

    [HttpDelete("{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<ApiResponse<StationDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Deactivate(int id, CancellationToken cancellationToken) =>
        FromResult(await _stations.DeactivateAsync(id, cancellationToken), message: "Station deactivated.");
}
