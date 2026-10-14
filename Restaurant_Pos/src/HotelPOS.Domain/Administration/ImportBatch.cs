using HotelPOS.Contracts.Admin;

namespace HotelPOS.Domain.Administration;

// One applied import and what it touched, so the latest one can be undone.
public sealed class ImportBatch
{
    private readonly List<ImportEntry> _entries = new();

    private ImportBatch()
    {
    }

    public ImportBatch(ImportKind kind, string fileName, DateTime nowUtc, int? userId)
    {
        Kind = kind;
        FileName = fileName.Length > 200 ? fileName[..200] : fileName;
        Status = ImportStatus.Applied;
        AppliedAt = nowUtc;
        AppliedBy = userId;
    }

    public int Id { get; private set; }
    public ImportKind Kind { get; private set; }
    public string FileName { get; private set; } = string.Empty;
    public ImportStatus Status { get; private set; }
    public int CreatedCount { get; private set; }
    public int UpdatedCount { get; private set; }
    public DateTime AppliedAt { get; private set; }
    public int? AppliedBy { get; private set; }
    public DateTime? UndoneAt { get; private set; }
    public int? UndoneBy { get; private set; }

    public IReadOnlyCollection<ImportEntry> Entries => _entries;

    public void Add(string entityType, int entityId, ImportAction action, string? beforeJson)
    {
        _entries.Add(new ImportEntry { Seq = _entries.Count + 1, EntityType = entityType, EntityId = entityId, Action = action, Before = beforeJson });
        if (action == ImportAction.Created)
        {
            CreatedCount++;
        }
        else
        {
            UpdatedCount++;
        }
    }

    public void MarkUndone(DateTime nowUtc, int? userId)
    {
        Status = ImportStatus.Undone;
        UndoneAt = nowUtc;
        UndoneBy = userId;
    }
}

public sealed class ImportEntry
{
    public int Id { get; set; }
    public int BatchId { get; set; }
    public int Seq { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public ImportAction Action { get; set; }

    // The entity's values before the import changed it (Updated), as JSON; null for Created.
    public string? Before { get; set; }
}
