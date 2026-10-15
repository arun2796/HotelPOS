using System.Net.Http.Json;
using HotelPOS.Contracts.Auth;
using HotelPOS.Contracts.Common;

namespace HotelPOS.Client.Services;

/// <summary>Browser: refresh token in the HttpOnly cookie set by /api/auth/web/login; the browser sends it by itself.</summary>
public sealed class CookieSessionTransport : ISessionTransport
{
    public const string RequestedWithHeader = "X-Requested-With";
    public const string RequestedWithValue = "HotelPOS.Web";

    private readonly HttpClient _http;
    private readonly IClientHost _host;

    public CookieSessionTransport(HttpClient http, IClientHost host)
    {
        _http = http;
        _host = host;
    }

    public Task<ApiResult<LoginResponse>> SignInAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, _host.Resolve("api/auth/web/login"))
        {
            Content = JsonContent.Create(request, options: PosJson.Options),
        };
        return SessionHttp.SendAsync(_http, message, cancellationToken);
    }

    public Task<ApiResult<LoginResponse>> RefreshAsync(CancellationToken cancellationToken)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, _host.Resolve("api/auth/web/refresh"));
        message.Headers.Add(RequestedWithHeader, RequestedWithValue);
        return SessionHttp.SendAsync(_http, message, cancellationToken);
    }

    public async Task SignOutAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, _host.Resolve("api/auth/web/logout"));
            using var response = await _http.SendAsync(message, cancellationToken);
        }
        catch (HttpRequestException)
        {
            // Offline: the cookie is cleared by the next successful call; the local session ends regardless.
        }
    }
}

/// <summary>Android app: the desktop-style flow — tokens in the body, the refresh token kept in a secure store.</summary>
public sealed class TokenSessionTransport : ISessionTransport
{
    private readonly HttpClient _http;
    private readonly IClientHost _host;
    private readonly ISecureTokenStore _store;

    public TokenSessionTransport(HttpClient http, IClientHost host, ISecureTokenStore store)
    {
        _http = http;
        _host = host;
        _store = store;
    }

    public async Task<ApiResult<LoginResponse>> SignInAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, _host.Resolve("api/auth/login"))
        {
            Content = JsonContent.Create(request, options: PosJson.Options),
        };
        var result = await SessionHttp.SendAsync(_http, message, cancellationToken);
        if (result.Success && result.Data is { } login)
        {
            await _store.SetRefreshTokenAsync(login.RefreshToken);
        }

        return result;
    }

    public async Task<ApiResult<LoginResponse>> RefreshAsync(CancellationToken cancellationToken)
    {
        var refreshToken = await _store.GetRefreshTokenAsync();
        if (string.IsNullOrEmpty(refreshToken))
        {
            return ApiResult<LoginResponse>.Fail(ErrorCodes.Unauthenticated, "Sign in again.");
        }

        var message = new HttpRequestMessage(HttpMethod.Post, _host.Resolve("api/auth/refresh"))
        {
            Content = JsonContent.Create(new RefreshTokenRequest { RefreshToken = refreshToken }, options: PosJson.Options),
        };
        var result = await SessionHttp.SendAsync(_http, message, cancellationToken);
        if (result.Success && result.Data is { } login)
        {
            await _store.SetRefreshTokenAsync(login.RefreshToken);
        }
        else if (!result.IsTransient)
        {
            // Revoked, expired or the device was deactivated: the stored token is worthless now.
            await _store.SetRefreshTokenAsync(null);
        }

        return result;
    }

    public async Task SignOutAsync(CancellationToken cancellationToken)
    {
        var refreshToken = await _store.GetRefreshTokenAsync();
        await _store.SetRefreshTokenAsync(null);
        if (string.IsNullOrEmpty(refreshToken))
        {
            return;
        }

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, _host.Resolve("api/auth/logout"))
            {
                Content = JsonContent.Create(new LogoutRequest { RefreshToken = refreshToken }, options: PosJson.Options),
            };
            using var response = await _http.SendAsync(message, cancellationToken);
        }
        catch (HttpRequestException)
        {
            // Offline: the server-side token expires on its own; the device no longer has it.
        }
    }
}

internal static class SessionHttp
{
    public static async Task<ApiResult<LoginResponse>> SendAsync(HttpClient http, HttpRequestMessage message, CancellationToken cancellationToken)
    {
        using (message)
        {
            try
            {
                using var response = await http.SendAsync(message, cancellationToken);
                return await WebApiClient.ReadAsync<LoginResponse>(response, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                return ApiResult<LoginResponse>.Fail(ErrorCodes.ConnectionUnavailable, "The server cannot be reached. Check the Wi-Fi and try again.");
            }
        }
    }
}
