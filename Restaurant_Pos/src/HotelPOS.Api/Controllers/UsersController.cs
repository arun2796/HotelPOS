using HotelPOS.Application.Features.Security.Commands;
using HotelPOS.Application.Features.Security.Queries;
using HotelPOS.Application.Features.Users.Commands;
using HotelPOS.Application.Features.Users.Queries;
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
    [HttpGet]
    [ProducesResponseType<ApiResponse<PagedResult<UserDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] UserQuery query, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new ListUsersQuery(query), cancellationToken));

    [HttpGet("{id:int}")]
    [ProducesResponseType<ApiResponse<UserDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetUserQuery(id), cancellationToken));

    [HttpPost]
    [ProducesResponseType<ApiResponse<UserDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateUserRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new CreateUserCommand(request), cancellationToken), StatusCodes.Status201Created, "User created.");

    [HttpPut("{id:int}")]
    [ProducesResponseType<ApiResponse<UserDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(int id, UpdateUserRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new UpdateUserCommand(id, request), cancellationToken), message: "User updated.");

    [HttpPost("{id:int}/activate")]
    public async Task<IActionResult> Activate(int id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new SetUserActiveCommand(id, Active: true), cancellationToken), message: "User activated.");

    [HttpPost("{id:int}/deactivate")]
    public async Task<IActionResult> Deactivate(int id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new SetUserActiveCommand(id, Active: false), cancellationToken), message: "User deactivated.");

    [HttpPost("{id:int}/reset-password")]
    public async Task<IActionResult> ResetPassword(int id, ResetPasswordRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new ResetUserPasswordCommand(id, request), cancellationToken), "Password reset.");

    [HttpPost("{id:int}/unlock")]
    public async Task<IActionResult> Unlock(int id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new UnlockUserCommand(id), cancellationToken), "User account unlocked.");

    [HttpGet("{id:int}/sessions")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<UserSessionDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSessions(int id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetUserSessionsQuery(id), cancellationToken));

    [HttpPost("{id:int}/logout-all")]
    public async Task<IActionResult> LogoutAll(int id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new LogoutUserEverywhereCommand(id), cancellationToken), "All user sessions terminated.");
}

[Route("api/roles")]
[Authorize(Policy = Permissions.RolesManage)]
public sealed class RolesController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<ApiResponse<IReadOnlyList<RoleDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new ListRolesQuery(), cancellationToken));

    [HttpGet("permissions")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<RolePermissionsDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAllPermissions(CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new ListRolePermissionsQuery(), cancellationToken));

    [HttpGet("{id:int}/permissions")]
    [ProducesResponseType<ApiResponse<RolePermissionsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPermissions(int id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetRolePermissionsQuery(id), cancellationToken));

    [HttpPut("{id:int}/permissions")]
    public async Task<IActionResult> UpdatePermissions(int id, UpdateRolePermissionsRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new UpdateRolePermissionsCommand(id, request), cancellationToken), "Role permissions updated.");
}
