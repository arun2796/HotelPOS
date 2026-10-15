using Microsoft.JSInterop;

namespace HotelPOS.Web.Services;

/// <summary>Key/value storage in the browser (localStorage). Never holds secrets: device name, drafts, preferences.</summary>
public interface IBrowserStorage
{
    ValueTask<string?> GetAsync(string key);

    ValueTask SetAsync(string key, string value);

    ValueTask RemoveAsync(string key);

    ValueTask<IReadOnlyList<string>> KeysAsync(string prefix);
}

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
