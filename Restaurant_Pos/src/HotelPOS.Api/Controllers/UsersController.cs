using HotelPOS.Application.Users;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Security;
using HotelPOS.Contracts.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelPOS.Api.Controllers;

[Route("api/users")]
[Authorize(Roles = Roles.Admin)]
public sealed class UsersController : ApiControllerBase
{
    private readonly IUserService _users;

    public UsersController(IUserService users)
    {
        _users = users;
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
}

[Route("api/roles")]
[Authorize(Roles = Roles.Admin)]
public sealed class RolesController : ApiControllerBase
{
    private readonly IUserService _users;

    public RolesController(IUserService users)
    {
        _users = users;
    }

    [HttpGet]
    [ProducesResponseType<ApiResponse<IReadOnlyList<RoleDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        Envelope(await _users.ListRolesAsync(cancellationToken));
}
