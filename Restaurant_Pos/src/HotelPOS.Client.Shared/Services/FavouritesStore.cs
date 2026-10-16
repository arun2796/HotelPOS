using System.Text.Json;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Menu;

namespace HotelPOS.Client.Services;

/// <summary>
/// Order Builder favourites (Phase 20.7), kept on this device only: the items the waiter pinned (in pin order) and how
/// often each item was sent from here. The "★ Favourites" chip lists the pinned items, then the most-ordered ones.
/// </summary>
public sealed class FavouritesStore
{
    public const string StorageKey = "hotelpos.favourites";

    /// <summary>Pseudo-category id of the Favourites chip (never a real category id).</summary>
    public const int CategoryId = -1;

    public const string CategoryName = "★ Favourites";

    /// <summary>How many most-ordered items follow the pinned ones, and how many counters are kept at all.</summary>
    public const int MostOrderedCount = 8;

    public const int MaxTrackedItems = 200;

    private readonly IBrowserStorage _storage;
    private Model _model = new();

    public FavouritesStore(IBrowserStorage storage)
    {
        _storage = storage;
    }

    public IReadOnlyList<int> Pinned => _model.Pinned;

    public IReadOnlyDictionary<int, int> Counts => _model.Counts;

    public bool IsPinned(int itemId) => _model.Pinned.Contains(itemId);

    public async ValueTask LoadAsync()
    {
        var json = await _storage.GetAsync(StorageKey);
        if (string.IsNullOrEmpty(json))
        {
            return;
        }

        try
        {
            _model = JsonSerializer.Deserialize<Model>(json, PosJson.Options) ?? new Model();
        }
        catch (JsonException)
        {
            _model = new Model();
        }
    }

    public ValueTask TogglePinAsync(int itemId)
    {
        var pinned = _model.Pinned.ToList();
        if (!pinned.Remove(itemId))
        {
            pinned.Add(itemId);
        }

        _model = _model with { Pinned = pinned };
        return SaveAsync();
    }

    /// <summary>Counts one more send of each distinct item; only the most-ordered <see cref="MaxTrackedItems"/> are kept.</summary>
    public ValueTask RecordOrderedAsync(IEnumerable<int> itemIds)
    {
        var counts = _model.Counts.ToDictionary(p => p.Key, p => p.Value);
        foreach (var id in itemIds.Distinct())
        {
            counts[id] = counts.GetValueOrDefault(id) + 1;
        }

        if (counts.Count > MaxTrackedItems)
        {
            counts = counts.OrderByDescending(p => p.Value).ThenBy(p => p.Key).Take(MaxTrackedItems).ToDictionary(p => p.Key, p => p.Value);
        }

        _model = _model with { Counts = counts };
        return SaveAsync();
    }

    /// <summary>Pinned items first, then the most-ordered ones on this device; only items still on the menu.</summary>
    public IReadOnlyList<MenuEntryDto> Items(MenuDto menu)
    {
        var byId = menu.Items.ToDictionary(i => i.Id);
        var pinned = _model.Pinned.Where(byId.ContainsKey).Distinct().ToList();
        var frequent = _model.Counts
            .Where(p => p.Value > 0 && byId.ContainsKey(p.Key) && !pinned.Contains(p.Key))
            .OrderByDescending(p => p.Value)
            .ThenBy(p => byId[p.Key].Name, StringComparer.OrdinalIgnoreCase)
            .Take(MostOrderedCount)
            .Select(p => p.Key);
        return pinned.Concat(frequent).Select(id => byId[id]).ToList();
    }

    /// <summary>Items whose name or code contains the text (case-insensitive), across every category.</summary>
    public static IEnumerable<MenuEntryDto> Search(MenuDto menu, string text)
    {
        var query = text.Trim();
        return menu.Items
            .Where(i => i.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || (i.Code?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
            .OrderBy(i => i.SortOrder);
    }

    private ValueTask SaveAsync() => _storage.SetAsync(StorageKey, JsonSerializer.Serialize(_model, PosJson.Options));

    private sealed record Model
    {
        public List<int> Pinned { get; init; } = new();

        public Dictionary<int, int> Counts { get; init; } = new();
    }
}
