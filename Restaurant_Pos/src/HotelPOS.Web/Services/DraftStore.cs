using System.Text.Json;
using HotelPOS.Contracts.Common;

namespace HotelPOS.Web.Services;

public sealed record WebDraftLine
{
    public int MenuItemId { get; init; }

    public string Name { get; init; } = string.Empty;

    public decimal UnitPrice { get; init; }

    public int Quantity { get; init; } = 1;

    public string? Notes { get; init; }

    public IReadOnlyList<int> ModifierOptionIds { get; init; } = Array.Empty<int>();

    public IReadOnlyList<string> ModifierNames { get; init; } = Array.Empty<string>();

    public decimal ModifierTotal { get; init; }
}

/// <summary>An unsent order kept on this device, per table — the browser twin of the desktop LocalDraft.</summary>
public sealed record WebDraft
{
    public int TableId { get; init; }

    public int? OrderId { get; init; }

    public int Guests { get; init; } = 1;

    public IReadOnlyList<WebDraftLine> Lines { get; init; } = Array.Empty<WebDraftLine>();

    /// <summary>The Idempotency-Key of the pending send, kept so a retry after a reload reuses it.</summary>
    public Guid? PendingKey { get; init; }

    public DateTime SavedAtUtc { get; init; }
}

public sealed class DraftStore
{
    private const string Prefix = "hotelpos.draft.";
    private readonly IBrowserStorage _storage;

    public DraftStore(IBrowserStorage storage)
    {
        _storage = storage;
    }

    public async ValueTask<WebDraft?> LoadAsync(int tableId)
    {
        var json = await _storage.GetAsync(Prefix + tableId);
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<WebDraft>(json, PosJson.Options);
        }
        catch (JsonException)
        {
            await _storage.RemoveAsync(Prefix + tableId);
            return null;
        }
    }

    public ValueTask SaveAsync(WebDraft draft) =>
        _storage.SetAsync(Prefix + draft.TableId, JsonSerializer.Serialize(draft with { SavedAtUtc = DateTime.UtcNow }, PosJson.Options));

    public ValueTask RemoveAsync(int tableId) => _storage.RemoveAsync(Prefix + tableId);

    public async ValueTask<IReadOnlyList<int>> TablesWithDraftsAsync()
    {
        var keys = await _storage.KeysAsync(Prefix);
        return keys.Select(k => int.TryParse(k[Prefix.Length..], out var id) ? id : -1).Where(id => id > 0).ToList();
    }
}
