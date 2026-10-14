namespace HotelPOS.Contracts.Admin;

public enum ImportKind
{
    Menu = 1,
    Customers = 2,
}

public enum ImportStatus
{
    Applied = 1,
    Undone = 2,
}

public enum ImportAction
{
    Created = 1,
    Updated = 2,
}

public static class ImportLimits
{
    public const int MaxRows = 5000;
    public const int MaxBytes = 5 * 1024 * 1024;
    public const int MaxErrorsListed = 200;
}

public sealed record ImportFieldDto
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public bool Required { get; init; }
    public string? Hint { get; init; }
}

public sealed record ImportRowErrorDto
{
    // The spreadsheet row number (the header is row 1).
    public int Row { get; init; }
    public string Field { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public sealed record ImportPreviewDto
{
    public ImportKind Kind { get; init; }
    public string FileName { get; init; } = string.Empty;
    public IReadOnlyList<string> Headers { get; init; } = Array.Empty<string>();
    public IReadOnlyList<ImportFieldDto> Fields { get; init; } = Array.Empty<ImportFieldDto>();

    // Field key -> header, as guessed from the header names, and as used for this preview.
    public IReadOnlyDictionary<string, string> SuggestedMapping { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> Mapping { get; init; } = new Dictionary<string, string>();
    public int TotalRows { get; init; }
    public int ValidRows { get; init; }
    public int ErrorRows { get; init; }
    public int ToCreate { get; init; }
    public int ToUpdate { get; init; }
    public IReadOnlyList<ImportRowErrorDto> Errors { get; init; } = Array.Empty<ImportRowErrorDto>();
    public bool ErrorsTruncated { get; init; }
}

public sealed record ImportResultDto
{
    public int BatchId { get; init; }
    public int Created { get; init; }
    public int Updated { get; init; }
    public int Skipped { get; init; }
}

public sealed record ImportBatchDto
{
    public int Id { get; init; }
    public ImportKind Kind { get; init; }
    public string FileName { get; init; } = string.Empty;
    public ImportStatus Status { get; init; }
    public int Created { get; init; }
    public int Updated { get; init; }
    public DateTime AppliedAtUtc { get; init; }
    public DateTime? UndoneAtUtc { get; init; }
    public bool CanUndo { get; init; }
}
