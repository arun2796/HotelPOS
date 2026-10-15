using HotelPOS.Client.Services;
using Microsoft.JSInterop;

namespace HotelPOS.Web.Services;

/// <summary>Browser host: localStorage through the small bridge in wwwroot/js/storage.js.</summary>
public sealed class JsBrowserStorage : IBrowserStorage
{
    private readonly IJSRuntime _js;

    public JsBrowserStorage(IJSRuntime js)
    {
        _js = js;
    }

    public ValueTask<string?> GetAsync(string key) => _js.InvokeAsync<string?>("hotelposStorage.get", key);

    public async ValueTask SetAsync(string key, string value) => await _js.InvokeAsync<bool>("hotelposStorage.set", key, value);

    public ValueTask RemoveAsync(string key) => _js.InvokeVoidAsync("hotelposStorage.remove", key);

    public async ValueTask<IReadOnlyList<string>> KeysAsync(string prefix) =>
        await _js.InvokeAsync<string[]>("hotelposStorage.keys", prefix);
}

/// <summary>Browser host: the server is the page's own origin (the app is served from /app by the API).</summary>
public sealed class BrowserClientHost : IClientHost
{
    public BrowserClientHost(Uri origin, string? appVersion)
    {
        ServerBase = origin;
        AppVersion = appVersion;
    }

    public Uri ServerBase { get; }

    public bool IsConfigured => true;

    public string DeviceNamePrefix => "WEB";

    public string? AppVersion { get; }

    public bool CanChangeServer => false;
}
