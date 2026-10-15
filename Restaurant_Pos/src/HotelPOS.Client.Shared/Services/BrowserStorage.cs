namespace HotelPOS.Client.Services;

/// <summary>
/// Key/value storage on the device: device name, drafts, preferences — never secrets. The browser host backs it with
/// localStorage, the Android host with Preferences and app files.
/// </summary>
public interface IBrowserStorage
{
    ValueTask<string?> GetAsync(string key);

    ValueTask SetAsync(string key, string value);

    ValueTask RemoveAsync(string key);

    ValueTask<IReadOnlyList<string>> KeysAsync(string prefix);
}
