using System.IO;
using System.Text.Json;
using HotelPOS.Contracts.Common;
using HotelPOS.Desktop.Services.Configuration;
using Microsoft.Extensions.Logging;

namespace HotelPOS.Desktop.Services.Orders;

public enum OrderBuilderMode
{
    NewOrder,
    EditDraft,
    AppendItems,
}

public sealed record DraftLine
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

public sealed record LocalDraft
{
    public int TableId { get; init; }
    public string TableCode { get; init; } = string.Empty;
    public OrderBuilderMode Mode { get; init; }
    public int? OrderId { get; init; }
    public int? OrderNumber { get; init; }
    public int GuestCount { get; init; }
    public string? Notes { get; init; }

    // Kept until the server gives a definitive answer, so a retry can never create a second order.
    public Guid? PendingKey { get; init; }

    public IReadOnlyList<DraftLine> Lines { get; init; } = Array.Empty<DraftLine>();
    public DateTime SavedAtUtc { get; init; }
}

public interface ILocalDraftStore
{
    LocalDraft? Load(int tableId);

    void Save(LocalDraft draft);

    void Delete(int tableId);

    bool Exists(int tableId);
}

public sealed class LocalDraftStore : ILocalDraftStore
{
    private readonly string _directory;
    private readonly ILogger<LocalDraftStore> _logger;

    public LocalDraftStore(AppPaths paths, ILogger<LocalDraftStore> logger)
    {
        _directory = Path.Combine(paths.UserDataDirectory, "drafts");
        _logger = logger;
    }

    public LocalDraft? Load(int tableId)
    {
        var path = PathFor(tableId);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<LocalDraft>(File.ReadAllText(path), PosJson.Options);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            _logger.LogWarning(ex, "Ignoring unreadable draft {Path}", path);
            return null;
        }
    }

    public void Save(LocalDraft draft)
    {
        Directory.CreateDirectory(_directory);
        var path = PathFor(draft.TableId);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(draft with { SavedAtUtc = DateTime.UtcNow }, PosJson.Options));
        File.Move(temp, path, overwrite: true);
    }

    public void Delete(int tableId)
    {
        var path = PathFor(tableId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public bool Exists(int tableId) => File.Exists(PathFor(tableId));

    private string PathFor(int tableId) => Path.Combine(_directory, $"table-{tableId}.json");
}
