using HotelPOS.Application.Security;
using HotelPOS.Application.Users;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Security;
using HotelPOS.Contracts.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelPOS.Api.Controllers;

[Route("api/users")]
[Authorize(Policy = Permissions.UsersManage)]
public sealed class UsersController : ApiControllerBase
{
    private readonly IUserService _users;
    private readonly ISessionService _sessions;

    public UsersController(IUserService users, ISessionService sessions)
    {
        _users = users;
        _sessions = sessions;
    }

    [HttpGet]
    [ProducesResponseType<ApiResponse<PagedResult<UserDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] UserQuery query, CancellationToken cancellationToken) =>
        Envelope(await _users.ListAsync(query, cancellationToken));

    [HttpGet("{id:int}")]
    [ProducesResponseType<ApiResponse<UserDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken) =>
        FromResult(await _users.GetAsync(id, cancellationToken));

    [HttpPost]
    [ProducesResponseType<ApiResponse<UserDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateUserRequest request, CancellationToken cancellationToken) =>
        FromResult(await _users.CreateAsync(request, cancellationToken), StatusCodes.Status201Created, "User created.");

    [HttpPut("{id:int}")]
    [ProducesResponseType<ApiResponse<UserDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(int id, UpdateUserRequest request, CancellationToken cancellationToken) =>
        FromResult(await _users.UpdateAsync(id, request, cancellationToken), message: "User updated.");

    [HttpPost("{id:int}/activate")]
    public async Task<IActionResult> Activate(int id, CancellationToken cancellationToken) =>
        FromResult(await _users.SetActiveAsync(id, true, cancellationToken), message: "User activated.");

    [HttpPost("{id:int}/deactivate")]
    public async Task<IActionResult> Deactivate(int id, CancellationToken cancellationToken) =>
        FromResult(await _users.SetActiveAsync(id, false, cancellationToken), message: "User deactivated.");

    [HttpPost("{id:int}/reset-password")]
    public async Task<IActionResult> ResetPassword(int id, ResetPasswordRequest request, CancellationToken cancellationToken) =>
        FromResult(await _users.ResetPasswordAsync(id, request, cancellationToken), "Password reset.");

    [HttpPost("{id:int}/unlock")]
    public async Task<IActionResult> Unlock(int id, CancellationToken cancellationToken) =>
        FromResult(await _sessions.UnlockUserAsync(id, cancellationToken), "User account unlocked.");

    [HttpGet("{id:int}/sessions")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<UserSessionDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSessions(int id, CancellationToken cancellationToken) =>
        FromResult(await _sessions.GetUserSessionsAsync(id, cancellationToken));

    [HttpPost("{id:int}/logout-all")]
    public async Task<IActionResult> LogoutAll(int id, CancellationToken cancellationToken) =>
        FromResult(await _sessions.LogoutAllAsync(id, cancellationToken), "All user sessions terminated.");
}

[Route("api/roles")]
[Authorize(Policy = Permissions.RolesManage)]
public sealed class RolesController : ApiControllerBase
{
    private readonly IUserService _users;
    private readonly IPermissionService _permissions;

    public RolesController(IUserService users, IPermissionService permissions)
    {
        _users = users;
        _permissions = permissions;
    }

    [HttpGet]
    [ProducesResponseType<ApiResponse<IReadOnlyList<RoleDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        Envelope(await _users.ListRolesAsync(cancellationToken));

    [HttpGet("permissions")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<RolePermissionsDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAllPermissions(CancellationToken cancellationToken) =>
        FromResult(await _permissions.GetAllRolePermissionsAsync(cancellationToken));

    [HttpGet("{id:int}/permissions")]
    [ProducesResponseType<ApiResponse<RolePermissionsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPermissions(int id, CancellationToken cancellationToken) =>
        FromResult(await _permissions.GetRolePermissionsAsync(id, cancellationToken));

    [HttpPut("{id:int}/permissions")]
    public async Task<IActionResult> UpdatePermissions(int id, UpdateRolePermissionsRequest request, CancellationToken cancellationToken) =>
        FromResult(await _permissions.UpdateRolePermissionsAsync(id, request, cancellationToken), "Role permissions updated.");
}
