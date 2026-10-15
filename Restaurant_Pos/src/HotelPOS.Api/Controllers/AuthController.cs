using HotelPOS.Application.Features.Auth.Commands;
using HotelPOS.Application.Features.Auth.Queries;
using HotelPOS.Application.Features.Security.Commands;
using HotelPOS.Contracts.Auth;
using HotelPOS.Contracts.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelPOS.Api.Controllers;

[Route("api/auth")]
public sealed class AuthController : ApiControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<LoginResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new LoginCommand(request), cancellationToken));

    [HttpPost("approval")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<ApprovalTokenResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Approval(ApprovalTokenRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new IssueApprovalTokenCommand(request), cancellationToken));

    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<LoginResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Refresh(RefreshTokenRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new RefreshTokenCommand(request), cancellationToken));

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(LogoutRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new LogoutCommand(request), cancellationToken), "Logged out.");

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<ApiResponse<CurrentUserDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Me(CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetCurrentUserQuery(), cancellationToken));

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new ChangePasswordCommand(request), cancellationToken), "Password changed.");
}
