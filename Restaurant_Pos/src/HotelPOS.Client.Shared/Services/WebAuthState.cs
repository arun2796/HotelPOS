using System.Security.Claims;
using HotelPOS.Contracts.Auth;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using Microsoft.AspNetCore.Components.Authorization;

namespace HotelPOS.Client.Services;

/// <summary>
/// The staff client's session: the access token lives only in memory; how the refresh token travels and is kept is
/// the host's business (<see cref="ISessionTransport"/>: cookie in the browser, SecureStorage in the Android app).
/// Roles and permissions come from the server's answer, never from the client.
/// </summary>
public sealed class WebAuthState : AuthenticationStateProvider
{
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(60);

    private readonly ISessionTransport _transport;
    private readonly IClientHost _host;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private string? _accessToken;
    private DateTime _expiresAtUtc;

    public WebAuthState(ISessionTransport transport, IClientHost host)
    {
        _transport = transport;
        _host = host;
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
        var result = await _transport.SignInAsync(new LoginRequest
        {
            Username = username,
            Password = password,
            DeviceName = deviceName,
            DeviceType = deviceType,
            AppVersion = _host.AppVersion,
        }, cancellationToken);

        if (result.Success && result.Data is { } login)
        {
            Apply(login);
            SessionEndedReason = null;
        }

        return result;
    }

    /// <summary>Called once at start-up: turns the stored session (cookie or secure store) back into a signed-in state.</summary>
    public async Task<bool> TryRestoreAsync() => _host.IsConfigured && await RefreshAsync();

    /// <summary>Single-flight refresh. Returns false when there is no valid session any more.</summary>
    public async Task<bool> RefreshAsync()
    {
        await _refreshLock.WaitAsync();
        try
        {
            var result = await _transport.RefreshAsync(CancellationToken.None);
            if (result.Success && result.Data is { } login)
            {
                Apply(login);
                return true;
            }

            if (IsSignedIn && !result.IsTransient)
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
        await _transport.SignOutAsync(CancellationToken.None);
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
}
