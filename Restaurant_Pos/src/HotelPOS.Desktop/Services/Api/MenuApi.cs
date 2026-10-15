using System.IO;
using System.Text;
using HotelPOS.Contracts.Menu;

namespace HotelPOS.Desktop.Services.Api;

public interface IMenuApi
{
    Task<ApiResult<MenuDto>> GetMenuAsync(int? knownVersion, CancellationToken cancellationToken = default);

    Task<ApiResult<List<CategoryDto>>> GetCategoriesAsync(bool includeInactive, CancellationToken cancellationToken = default);

    Task<ApiResult<CategoryDto>> CreateCategoryAsync(CreateCategoryRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<CategoryDto>> UpdateCategoryAsync(int id, UpdateCategoryRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<List<MenuItemDto>>> GetItemsAsync(MenuItemQuery query, CancellationToken cancellationToken = default);

    Task<ApiResult<MenuItemDto>> CreateItemAsync(CreateMenuItemRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<MenuItemDto>> UpdateItemAsync(int id, UpdateMenuItemRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<MenuItemDto>> SetAvailabilityAsync(int id, bool isAvailable, CancellationToken cancellationToken = default);

    Task<ApiResult<MenuItemDto>> UploadImageAsync(int id, Stream content, string fileName, CancellationToken cancellationToken = default);

    Task<ApiResult<MenuItemDto>> RemoveImageAsync(int id, CancellationToken cancellationToken = default);

    Task<ApiResult<List<ModifierGroupDto>>> GetModifierGroupsAsync(bool includeInactive, CancellationToken cancellationToken = default);

    Task<ApiResult<ModifierGroupDto>> CreateModifierGroupAsync(SaveModifierGroupRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<ModifierGroupDto>> UpdateModifierGroupAsync(int id, SaveModifierGroupRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<ModifierGroupDto>> AddOptionAsync(int groupId, SaveModifierOptionRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<ModifierGroupDto>> UpdateOptionAsync(int groupId, int optionId, SaveModifierOptionRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<List<TaxDto>>> GetTaxesAsync(bool includeInactive, CancellationToken cancellationToken = default);

    Task<ApiResult<TaxDto>> CreateTaxAsync(SaveTaxRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<TaxDto>> UpdateTaxAsync(int id, SaveTaxRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<List<StationDto>>> GetStationsAsync(bool includeInactive, CancellationToken cancellationToken = default);

    Task<ApiResult<StationDto>> CreateStationAsync(SaveStationRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<StationDto>> UpdateStationAsync(int id, SaveStationRequest request, CancellationToken cancellationToken = default);
}

public sealed class MenuApi : IMenuApi
{
    private readonly IApiClient _api;

    public MenuApi(IApiClient api)
    {
        _api = api;
    }

    public Task<ApiResult<MenuDto>> GetMenuAsync(int? knownVersion, CancellationToken cancellationToken = default) =>
        _api.GetAsync<MenuDto>(knownVersion is { } v ? $"api/menu?version={v}" : "api/menu", cancellationToken);

    public Task<ApiResult<List<CategoryDto>>> GetCategoriesAsync(bool includeInactive, CancellationToken cancellationToken = default) =>
        _api.GetAsync<List<CategoryDto>>(WithInactive("api/categories", includeInactive), cancellationToken);

    public Task<ApiResult<CategoryDto>> CreateCategoryAsync(CreateCategoryRequest request, CancellationToken cancellationToken = default) =>
        _api.PostAsync<CategoryDto>("api/categories", request, cancellationToken);

    public Task<ApiResult<CategoryDto>> UpdateCategoryAsync(int id, UpdateCategoryRequest request, CancellationToken cancellationToken = default) =>
        _api.PutAsync<CategoryDto>($"api/categories/{id}", request, cancellationToken);

    public Task<ApiResult<List<MenuItemDto>>> GetItemsAsync(MenuItemQuery query, CancellationToken cancellationToken = default)
    {
        var url = new StringBuilder("api/menu-items?includeInactive=").Append(query.IncludeInactive ? "true" : "false");
        if (query.CategoryId is { } categoryId)
        {
            url.Append("&categoryId=").Append(categoryId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            url.Append("&search=").Append(Uri.EscapeDataString(query.Search.Trim()));
        }

        return _api.GetAsync<List<MenuItemDto>>(url.ToString(), cancellationToken);
    }

    public Task<ApiResult<MenuItemDto>> CreateItemAsync(CreateMenuItemRequest request, CancellationToken cancellationToken = default) =>
        _api.PostAsync<MenuItemDto>("api/menu-items", request, cancellationToken);

    public Task<ApiResult<MenuItemDto>> UpdateItemAsync(int id, UpdateMenuItemRequest request, CancellationToken cancellationToken = default) =>
        _api.PutAsync<MenuItemDto>($"api/menu-items/{id}", request, cancellationToken);

    public Task<ApiResult<MenuItemDto>> SetAvailabilityAsync(int id, bool isAvailable, CancellationToken cancellationToken = default) =>
        _api.PatchAsync<MenuItemDto>($"api/menu-items/{id}/availability", new SetAvailabilityRequest { IsAvailable = isAvailable }, cancellationToken);

    public Task<ApiResult<MenuItemDto>> UploadImageAsync(int id, Stream content, string fileName, CancellationToken cancellationToken = default) =>
        _api.UploadAsync<MenuItemDto>($"api/menu-items/{id}/image", content, fileName,
            Path.GetExtension(fileName).Equals(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg",
            cancellationToken);

    public Task<ApiResult<MenuItemDto>> RemoveImageAsync(int id, CancellationToken cancellationToken = default) =>
        _api.DeleteAsync<MenuItemDto>($"api/menu-items/{id}/image", cancellationToken);

    public Task<ApiResult<List<ModifierGroupDto>>> GetModifierGroupsAsync(bool includeInactive, CancellationToken cancellationToken = default) =>
        _api.GetAsync<List<ModifierGroupDto>>(WithInactive("api/modifier-groups", includeInactive), cancellationToken);

    public Task<ApiResult<ModifierGroupDto>> CreateModifierGroupAsync(SaveModifierGroupRequest request, CancellationToken cancellationToken = default) =>
        _api.PostAsync<ModifierGroupDto>("api/modifier-groups", request, cancellationToken);

    public Task<ApiResult<ModifierGroupDto>> UpdateModifierGroupAsync(int id, SaveModifierGroupRequest request, CancellationToken cancellationToken = default) =>
        _api.PutAsync<ModifierGroupDto>($"api/modifier-groups/{id}", request, cancellationToken);

    public Task<ApiResult<ModifierGroupDto>> AddOptionAsync(int groupId, SaveModifierOptionRequest request, CancellationToken cancellationToken = default) =>
        _api.PostAsync<ModifierGroupDto>($"api/modifier-groups/{groupId}/options", request, cancellationToken);

    public Task<ApiResult<ModifierGroupDto>> UpdateOptionAsync(int groupId, int optionId, SaveModifierOptionRequest request, CancellationToken cancellationToken = default) =>
        _api.PutAsync<ModifierGroupDto>($"api/modifier-groups/{groupId}/options/{optionId}", request, cancellationToken);

    public Task<ApiResult<List<TaxDto>>> GetTaxesAsync(bool includeInactive, CancellationToken cancellationToken = default) =>
        _api.GetAsync<List<TaxDto>>(WithInactive("api/taxes", includeInactive), cancellationToken);

    public Task<ApiResult<TaxDto>> CreateTaxAsync(SaveTaxRequest request, CancellationToken cancellationToken = default) =>
        _api.PostAsync<TaxDto>("api/taxes", request, cancellationToken);

    public Task<ApiResult<TaxDto>> UpdateTaxAsync(int id, SaveTaxRequest request, CancellationToken cancellationToken = default) =>
        _api.PutAsync<TaxDto>($"api/taxes/{id}", request, cancellationToken);

    public Task<ApiResult<List<StationDto>>> GetStationsAsync(bool includeInactive, CancellationToken cancellationToken = default) =>
        _api.GetAsync<List<StationDto>>(WithInactive("api/stations", includeInactive), cancellationToken);

    public Task<ApiResult<StationDto>> CreateStationAsync(SaveStationRequest request, CancellationToken cancellationToken = default) =>
        _api.PostAsync<StationDto>("api/stations", request, cancellationToken);

    public Task<ApiResult<StationDto>> UpdateStationAsync(int id, SaveStationRequest request, CancellationToken cancellationToken = default) =>
        _api.PutAsync<StationDto>($"api/stations/{id}", request, cancellationToken);

    private static string WithInactive(string path, bool includeInactive) => includeInactive ? path + "?includeInactive=true" : path;
}
