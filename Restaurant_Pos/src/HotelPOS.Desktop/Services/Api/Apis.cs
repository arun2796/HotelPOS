using System.Globalization;
using System.Text;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Auth;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Users;

namespace HotelPOS.Desktop.Services.Api;

public interface IAuthApi
{
    Task<ApiResult<LoginResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<LoginResponse>> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);

    Task<ApiResult<object>> LogoutAsync(string refreshToken, CancellationToken cancellationToken = default);

    Task<ApiResult<object>> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken = default);
}

public sealed class AuthApi : IAuthApi
{
    private static readonly ApiRequestOptions Anonymous = new() { Anonymous = true };
    private readonly IApiClient _api;

    public AuthApi(IApiClient api)
    {
        _api = api;
    }

    public Task<ApiResult<LoginResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default) =>
        _api.PostAsync<LoginResponse>("api/auth/login", request, cancellationToken, Anonymous);

    public Task<ApiResult<LoginResponse>> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default) =>
        _api.PostAsync<LoginResponse>("api/auth/refresh", new RefreshTokenRequest { RefreshToken = refreshToken }, cancellationToken, Anonymous);

    public Task<ApiResult<object>> LogoutAsync(string refreshToken, CancellationToken cancellationToken = default) =>
        _api.PostAsync<object>("api/auth/logout", new LogoutRequest { RefreshToken = refreshToken }, cancellationToken, Anonymous);

    public Task<ApiResult<object>> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken = default) =>
        _api.PostAsync<object>("api/auth/change-password", request, cancellationToken);
}

public interface ISystemApi
{
    Task<ApiResult<SystemInfoDto>> GetInfoAsync(string? baseUrlOverride = null, CancellationToken cancellationToken = default);

    Task<ApiResult<List<SettingDto>>> GetPublicSettingsAsync(CancellationToken cancellationToken = default);
}

public sealed class SystemApi : ISystemApi
{
    private readonly IApiClient _api;

    public SystemApi(IApiClient api)
    {
        _api = api;
    }

    public Task<ApiResult<SystemInfoDto>> GetInfoAsync(string? baseUrlOverride = null, CancellationToken cancellationToken = default) =>
        _api.GetAsync<SystemInfoDto>("api/system/info", cancellationToken, new ApiRequestOptions
        {
            Anonymous = true,
            BaseUrlOverride = baseUrlOverride,
            Timeout = TimeSpan.FromSeconds(5),
            RetryReads = false,
        });

    public Task<ApiResult<List<SettingDto>>> GetPublicSettingsAsync(CancellationToken cancellationToken = default) =>
        _api.GetAsync<List<SettingDto>>("api/settings", cancellationToken);
}

public interface IUsersApi
{
    Task<ApiResult<PagedResult<UserDto>>> ListAsync(UserQuery query, CancellationToken cancellationToken = default);

    Task<ApiResult<UserDto>> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<UserDto>> UpdateAsync(int id, UpdateUserRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<UserDto>> SetActiveAsync(int id, bool active, CancellationToken cancellationToken = default);

    Task<ApiResult<object>> ResetPasswordAsync(int id, ResetPasswordRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<List<RoleDto>>> GetRolesAsync(CancellationToken cancellationToken = default);
}

public sealed class UsersApi : IUsersApi
{
    private readonly IApiClient _api;

    public UsersApi(IApiClient api)
    {
        _api = api;
    }

    public Task<ApiResult<PagedResult<UserDto>>> ListAsync(UserQuery query, CancellationToken cancellationToken = default)
    {
        var url = new StringBuilder("api/users?page=").Append(query.Page.ToString(CultureInfo.InvariantCulture))
            .Append("&pageSize=").Append(query.PageSize.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            url.Append("&search=").Append(Uri.EscapeDataString(query.Search.Trim()));
        }

        if (query.IsActive is { } active)
        {
            url.Append("&isActive=").Append(active ? "true" : "false");
        }

        return _api.GetAsync<PagedResult<UserDto>>(url.ToString(), cancellationToken);
    }

    public Task<ApiResult<UserDto>> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken = default) =>
        _api.PostAsync<UserDto>("api/users", request, cancellationToken);

    public Task<ApiResult<UserDto>> UpdateAsync(int id, UpdateUserRequest request, CancellationToken cancellationToken = default) =>
        _api.PutAsync<UserDto>($"api/users/{id}", request, cancellationToken);

    public Task<ApiResult<UserDto>> SetActiveAsync(int id, bool active, CancellationToken cancellationToken = default) =>
        _api.PostAsync<UserDto>($"api/users/{id}/{(active ? "activate" : "deactivate")}", null, cancellationToken);

    public Task<ApiResult<object>> ResetPasswordAsync(int id, ResetPasswordRequest request, CancellationToken cancellationToken = default) =>
        _api.PostAsync<object>($"api/users/{id}/reset-password", request, cancellationToken);

    public Task<ApiResult<List<RoleDto>>> GetRolesAsync(CancellationToken cancellationToken = default) =>
        _api.GetAsync<List<RoleDto>>("api/roles", cancellationToken);
}

public interface IAdminSettingsApi
{
    Task<ApiResult<List<SettingDto>>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<List<SettingDto>>> UpdateAsync(UpdateSettingsRequest request, CancellationToken cancellationToken = default);
}

public sealed class AdminSettingsApi : IAdminSettingsApi
{
    private readonly IApiClient _api;

    public AdminSettingsApi(IApiClient api)
    {
        _api = api;
    }

    public Task<ApiResult<List<SettingDto>>> GetAllAsync(CancellationToken cancellationToken = default) =>
        _api.GetAsync<List<SettingDto>>("api/admin/settings", cancellationToken);

    public Task<ApiResult<List<SettingDto>>> UpdateAsync(UpdateSettingsRequest request, CancellationToken cancellationToken = default) =>
        _api.PutAsync<List<SettingDto>>("api/admin/settings", request, cancellationToken);
}

public interface IFloorApi
{
    Task<ApiResult<TableMapDto>> GetMapAsync(bool includeInactive = false, CancellationToken cancellationToken = default);

    Task<ApiResult<TableDetailDto>> GetTableAsync(int id, CancellationToken cancellationToken = default);

    Task<ApiResult<TableDto>> OccupyAsync(int id, OccupyTableRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<TableDto>> ReleaseAsync(int id, CancellationToken cancellationToken = default);

    Task<ApiResult<TableDto>> SetOutOfServiceAsync(int id, bool outOfService, CancellationToken cancellationToken = default);

    Task<ApiResult<TableDto>> CreateTableAsync(CreateTableRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<TableDto>> UpdateTableAsync(int id, UpdateTableRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<List<SectionDto>>> GetSectionsAsync(bool includeInactive = false, CancellationToken cancellationToken = default);

    Task<ApiResult<SectionDto>> CreateSectionAsync(CreateSectionRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<SectionDto>> UpdateSectionAsync(int id, UpdateSectionRequest request, CancellationToken cancellationToken = default);
}

public sealed class FloorApi : IFloorApi
{
    private readonly IApiClient _api;

    public FloorApi(IApiClient api)
    {
        _api = api;
    }

    public Task<ApiResult<TableMapDto>> GetMapAsync(bool includeInactive = false, CancellationToken cancellationToken = default) =>
        _api.GetAsync<TableMapDto>(includeInactive ? "api/tables?includeInactive=true" : "api/tables", cancellationToken);

    public Task<ApiResult<TableDetailDto>> GetTableAsync(int id, CancellationToken cancellationToken = default) =>
        _api.GetAsync<TableDetailDto>($"api/tables/{id}", cancellationToken);

    public Task<ApiResult<TableDto>> OccupyAsync(int id, OccupyTableRequest request, CancellationToken cancellationToken = default) =>
        _api.PostAsync<TableDto>($"api/tables/{id}/occupy", request, cancellationToken);

    public Task<ApiResult<TableDto>> ReleaseAsync(int id, CancellationToken cancellationToken = default) =>
        _api.PostAsync<TableDto>($"api/tables/{id}/release", null, cancellationToken);

    public Task<ApiResult<TableDto>> SetOutOfServiceAsync(int id, bool outOfService, CancellationToken cancellationToken = default) =>
        _api.PostAsync<TableDto>($"api/tables/{id}/{(outOfService ? "out-of-service" : "in-service")}", null, cancellationToken);

    public Task<ApiResult<TableDto>> CreateTableAsync(CreateTableRequest request, CancellationToken cancellationToken = default) =>
        _api.PostAsync<TableDto>("api/tables", request, cancellationToken);

    public Task<ApiResult<TableDto>> UpdateTableAsync(int id, UpdateTableRequest request, CancellationToken cancellationToken = default) =>
        _api.PutAsync<TableDto>($"api/tables/{id}", request, cancellationToken);

    public Task<ApiResult<List<SectionDto>>> GetSectionsAsync(bool includeInactive = false, CancellationToken cancellationToken = default) =>
        _api.GetAsync<List<SectionDto>>(includeInactive ? "api/sections?includeInactive=true" : "api/sections", cancellationToken);

    public Task<ApiResult<SectionDto>> CreateSectionAsync(CreateSectionRequest request, CancellationToken cancellationToken = default) =>
        _api.PostAsync<SectionDto>("api/sections", request, cancellationToken);

    public Task<ApiResult<SectionDto>> UpdateSectionAsync(int id, UpdateSectionRequest request, CancellationToken cancellationToken = default) =>
        _api.PutAsync<SectionDto>($"api/sections/{id}", request, cancellationToken);
}
