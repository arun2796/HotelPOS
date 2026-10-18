using HotelPOS.Contracts.Auth;

namespace HotelPOS.Client.Services;

/// <summary>
/// What the host (browser page or Android app) knows and the shared pages do not: where the server is, what to call
/// this kind of device, and the app version sent at sign-in. The server address may change at runtime on Android
/// (the Server page), so it is read through this interface on every call rather than baked into an HttpClient.
/// </summary>
public interface IClientHost
{
    /// <summary>Absolute base of the API, with a trailing slash, e.g. https://192.168.1.12:5001/.</summary>
    Uri ServerBase { get; }

    /// <summary>True once a server address is known; the browser always is, the app after its first-run page.</summary>
    bool IsConfigured { get; }

    /// <summary>Prefix of generated device names: WEB for a browser, APP for the Android app.</summary>
    string DeviceNamePrefix { get; }

    string? AppVersion { get; }

    /// <summary>True when the host has a Server page (the Android app); the browser is always at the server.</summary>
    bool CanChangeServer { get; }
}

public static class ClientHostExtensions
{
    public static Uri Resolve(this IClientHost host, string relativePath) =>
        new(host.ServerBase, relativePath.TrimStart('/'));

    /// <summary>Menu pictures come back as "/images/menu/x.jpg"; inside a native WebView that would point at the page's own origin.</summary>
    // Only an http(s) URL is already absolute: Uri.TryCreate(…, Absolute) also accepts "/images/menu/x.jpg" as a
    // file path on Linux-style parsing, which turned every menu picture into file:///images/… in the browser.
    public static string? ImageUrl(this IClientHost host, string? imageUrl) =>
        string.IsNullOrEmpty(imageUrl) ? null
        : Uri.TryCreate(imageUrl, UriKind.Absolute, out var absolute) && absolute.Scheme is "http" or "https" ? absolute.ToString()
        : host.Resolve(imageUrl).ToString();
}

/// <summary>How sign-in, refresh and sign-out travel to the server. The browser uses the cookie endpoints
/// (/api/auth/web/*); the Android app the token endpoints (/api/auth/*) with the refresh token in SecureStorage.</summary>
public interface ISessionTransport
{
    Task<ApiResult<LoginResponse>> SignInAsync(LoginRequest request, CancellationToken cancellationToken);

    /// <summary>Returns a failed result (not an exception) when there is no session to restore.</summary>
    Task<ApiResult<LoginResponse>> RefreshAsync(CancellationToken cancellationToken);

    Task SignOutAsync(CancellationToken cancellationToken);
}

/// <summary>Where the Android app keeps the refresh token (SecureStorage). The browser has no such store: the cookie is the store.</summary>
public interface ISecureTokenStore
{
    ValueTask<string?> GetRefreshTokenAsync();

    ValueTask SetRefreshTokenAsync(string? refreshToken);
}
