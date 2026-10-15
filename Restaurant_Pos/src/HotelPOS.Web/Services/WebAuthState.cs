using System.Net.Http.Json;
using System.Security.Claims;
using HotelPOS.Contracts.Auth;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using Microsoft.AspNetCore.Components.Authorization;

namespace HotelPOS.Web.Services;

/// <summary>
/// Browser session: the access token lives only in memory; the refresh token is an HttpOnly cookie that the browser
/// sends by itself to /api/auth/web/*. Roles and permissions come from the server's answer, never from the client.
/// </summary>
public sealed class WebAuthState : AuthenticationStateProvider
{
    public const string RequestedWithHeader = "X-Requested-With";
    public const string RequestedWithValue = "HotelPOS.Web";
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(60);

    private readonly HttpClient _http;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private string? _accessToken;
    private DateTime _expiresAtUtc;

    public WebAuthState(HttpClient http)
    {
        _http = http;
    }

    public CurrentUserDto? User { get; private set; }

    public Guid? DeviceId { get; private set; }

    public bool IsSignedIn => _accessToken is not null && User is not null;

    /// <summary>Why the last session ended (shown on the sign-in screen), if it ended on its own.</summary>
    public string? SessionEndedReason { get; private set; }

    public bool Has(string permission) => User?.Permissions.Contains(permission, StringComparer.Ordinal) == true;

    public bool IsInRole(string role) => User?.Roles.Contains(role, StringComparer.OrdinalIgnoreCase) == true;

    public async Task<ApiResult<LoginResponse>> SignInAsync(string username, string password, string deviceName, DeviceType deviceType, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/web/login")
        {
            Content = JsonContent.Create(new LoginRequest
            {
                Username = username,
                Password = password,
                DeviceName = deviceName,
                DeviceType = deviceType,
                AppVersion = typeof(WebAuthState).Assembly.GetName().Version?.ToString(3),
            }, options: PosJson.Options),
        };

        var result = await SendAsync(request, cancellationToken);
        if (result.Success && result.Data is { } login)
        {
            Apply(login);
            SessionEndedReason = null;
        }

        return result;
    }

    /// <summary>Called once at start-up: turns the refresh cookie (if any) back into a session.</summary>
    public async Task<bool> TryRestoreAsync() => await RefreshAsync();

    /// <summary>Single-flight refresh through the cookie. Returns false when there is no valid session any more.</summary>
    public async Task<bool> RefreshAsync()
    {
        await _refreshLock.WaitAsync();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/web/refresh");
            request.Headers.Add(RequestedWithHeader, RequestedWithValue);
            var result = await SendAsync(request, CancellationToken.None);
            if (result.Success && result.Data is { } login)
            {
                Apply(login);
                return true;
            }

            if (IsSignedIn)
            {
                await SignOutLocallyAsync(result.ErrorCode == ErrorCodes.DeviceDisabled
                    ? "This device was deactivated by an administrator."
                    : "Your session has ended. Sign in again.");
            }

            return false;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public async ValueTask<string?> GetAccessTokenAsync()
    {
        if (_accessToken is null)
        {
            return null;
        }

        if (DateTime.UtcNow >= _expiresAtUtc - RefreshMargin)
        {
            await RefreshAsync();
        }

        return _accessToken;
    }

    public async Task SignOutAsync()
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/web/logout");
            using var response = await _http.SendAsync(request);
        }
        catch (HttpRequestException)
        {
            // Offline: the cookie is cleared on the next successful call; the local session ends now regardless.
        }

        await SignOutLocallyAsync(null);
    }

    public Task SignOutLocallyAsync(string? reason)
    {
        _accessToken = null;
        _expiresAtUtc = default;
        User = null;
        DeviceId = null;
        SessionEndedReason = reason;
        NotifyAuthenticationStateChanged(Task.FromResult(BuildState()));
        return Task.CompletedTask;
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(BuildState());

    private void Apply(LoginResponse login)
    {
        _accessToken = login.AccessToken;
        _expiresAtUtc = login.AccessTokenExpiresAtUtc;
        User = login.User;
        DeviceId = login.DeviceId;
        NotifyAuthenticationStateChanged(Task.FromResult(BuildState()));
    }

    private AuthenticationState BuildState()
    {
        if (!IsSignedIn || User is null)
        {
            return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, User.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(ClaimTypes.Name, User.Username),
            new("display_name", User.DisplayName),
        };
        claims.AddRange(User.Roles.Select(r => new Claim(ClaimTypes.Role, r)));
        claims.AddRange(User.Permissions.Select(p => new Claim("perm", p)));
        return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(claims, "HotelPOS")));
    }

    private async Task<ApiResult<LoginResponse>> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.SendAsync(request, cancellationToken);
            return await WebApiClient.ReadAsync<LoginResponse>(response, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return ApiResult<LoginResponse>.Fail(ErrorCodes.ConnectionUnavailable, "The server cannot be reached. Check the Wi-Fi and try again.");
        }
    }
}
