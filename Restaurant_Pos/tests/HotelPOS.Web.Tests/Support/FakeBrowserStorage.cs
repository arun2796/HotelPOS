using HotelPOS.Web.Services;

namespace HotelPOS.Web.Tests.Support;

public sealed class FakeBrowserStorage : IBrowserStorage
{
    public Dictionary<string, string> Items { get; } = new(StringComparer.Ordinal);

    public ValueTask<string?> GetAsync(string key) => new(Items.TryGetValue(key, out var value) ? value : null);

    public ValueTask SetAsync(string key, string value)
    {
        Items[key] = value;
        return default;
    }

    public ValueTask RemoveAsync(string key)
    {
        Items.Remove(key);
        return default;
    }

    public ValueTask<IReadOnlyList<string>> KeysAsync(string prefix) =>
        new(Items.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList());
}
