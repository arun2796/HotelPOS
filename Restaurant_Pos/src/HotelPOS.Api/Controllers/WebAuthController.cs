using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Features.Auth.Commands;
using HotelPOS.Contracts.Auth;
using HotelPOS.Contracts.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelPOS.Api.Controllers;

/// <summary>
/// Sign-in for the browser client (Phase 18). Same commands as <see cref="AuthController"/>; the only difference is
/// where the refresh token lives: never in the response body, always in an HttpOnly cookie scoped to this route, so a
/// script running in the page cannot read it. The access token is returned as usual and kept in memory by the client.
/// </summary>
[Route("api/auth/web")]
public sealed class WebAuthController : ApiControllerBase
{
    public const string CookieName = "hp_rt";
    public const string RequestedWithHeader = "X-Requested-With";
    public const string RequestedWithValue = "HotelPOS.Web";

    // The cookie is only sent to these three endpoints, never to the rest of the API.
    private const string CookiePath = "/api/auth/web";

    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<LoginResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new LoginCommand(request), cancellationToken);
        if (result.IsFailure)
        {
            return Failure(result.Error!);
        }

        SetRefreshCookie(result.Value.RefreshToken, result.Value.RefreshTokenExpiresAtUtc);
        return Envelope(WithoutRefreshToken(result.Value));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<LoginResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        // A plain form post from another site cannot add this header; together with SameSite=Strict it keeps the
        // cookie from being replayed by a page the user did not open.
        if (!IsFromWebClient())
        {
            return Failure(new AppError(ErrorCodes.Forbidden, $"The {RequestedWithHeader} header is required."));
        }

        if (!Request.Cookies.TryGetValue(CookieName, out var refreshToken) || string.IsNullOrWhiteSpace(refreshToken))
        {
            return Failure(new AppError(ErrorCodes.Unauthenticated, "Sign in again."));
        }

        var result = await Sender.Send(new RefreshTokenCommand(new RefreshTokenRequest { RefreshToken = refreshToken }), cancellationToken);
        if (result.IsFailure)
        {
            ClearRefreshCookie();
            return Failure(result.Error!);
        }

        SetRefreshCookie(result.Value.RefreshToken, result.Value.RefreshTokenExpiresAtUtc);
        return Envelope(WithoutRefreshToken(result.Value));
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        if (Request.Cookies.TryGetValue(CookieName, out var refreshToken) && !string.IsNullOrWhiteSpace(refreshToken))
        {
            // Revoke server-side like the desktop logout; a stale or unknown token is not an error for the caller.
            await Sender.Send(new LogoutCommand(new LogoutRequest { RefreshToken = refreshToken }), cancellationToken);
        }

        ClearRefreshCookie();
        return Envelope<object?>(null, message: "Logged out.");
    }

    private bool IsFromWebClient() =>
        Request.Headers.TryGetValue(RequestedWithHeader, out var values)
        && string.Equals(values.ToString(), RequestedWithValue, StringComparison.Ordinal);

    private static LoginResponse WithoutRefreshToken(LoginResponse response) =>
        response with { RefreshToken = string.Empty };

    private void SetRefreshCookie(string token, DateTime expiresAtUtc) =>
        Response.Cookies.Append(CookieName, token, CookieOptions(expiresAtUtc));

    private void ClearRefreshCookie() =>
        Response.Cookies.Delete(CookieName, CookieOptions(expires: null));

    // Secure follows the request scheme: on HTTPS the browser refuses to send the cookie over plain HTTP; on a plain-HTTP
    // staff network (docs/ops/02 § 5) a Secure cookie would never be stored at all.
    private CookieOptions CookieOptions(DateTime? expires) => new()
    {
        HttpOnly = true,
        Secure = Request.IsHttps,
        SameSite = SameSiteMode.Strict,
        Path = CookiePath,
        Expires = expires is { } utc ? new DateTimeOffset(utc, TimeSpan.Zero) : null,
        IsEssential = true,
    };
}
