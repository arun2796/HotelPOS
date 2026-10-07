using HotelPOS.Application.Auth;
using HotelPOS.Contracts.Auth;
using HotelPOS.Contracts.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelPOS.Api.Controllers;

[Route("api/auth")]
public sealed class AuthController : ApiControllerBase
{
    private readonly IAuthService _auth;

    public AuthController(IAuthService auth)
    {
        _auth = auth;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<LoginResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken) =>
        FromResult(await _auth.LoginAsync(request, cancellationToken));

    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<LoginResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Refresh(RefreshTokenRequest request, CancellationToken cancellationToken) =>
        FromResult(await _auth.RefreshAsync(request, cancellationToken));

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(LogoutRequest request, CancellationToken cancellationToken) =>
        FromResult(await _auth.LogoutAsync(request, cancellationToken), "Logged out.");

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<ApiResponse<CurrentUserDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Me(CancellationToken cancellationToken) =>
        FromResult(await _auth.GetCurrentUserAsync(cancellationToken));

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken) =>
        FromResult(await _auth.ChangePasswordAsync(request, cancellationToken), "Password changed.");
}
