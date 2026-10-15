using HotelPOS.Contracts.Enums;

namespace HotelPOS.Contracts.Admin;

public sealed record BackupDto
{
    public int Id { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string? MediaFileName { get; init; }
    public long SizeBytes { get; init; }
    public BackupStatus Status { get; init; }
    public BackupTrigger Trigger { get; init; }
    public string? TriggeredBy { get; init; }
    public DateTime StartedAtUtc { get; init; }
    public DateTime? FinishedAtUtc { get; init; }
    public string? Error { get; init; }
}

public sealed record BackupOverviewDto
{
    public string Folder { get; init; } = string.Empty;
    public bool Enabled { get; init; }
    public string Time { get; init; } = string.Empty;
    public int RetentionDays { get; init; }
    public BackupDto? LastFailure { get; init; }
    public IReadOnlyList<BackupDto> Backups { get; init; } = Array.Empty<BackupDto>();
}

public enum NoticeLevel
{
    Info = 1,
    Warning = 2,
    Critical = 3,
}

public sealed record ServerNoticeRequest
{
    public NoticeLevel Level { get; init; } = NoticeLevel.Info;
    public string Message { get; init; } = string.Empty;

    public const int MaxMessageLength = 300;
}

public sealed record LogTailQuery
{
    public const int MaxLines = 2000;

    public int Lines { get; init; } = 500;

    // Serilog level abbreviation as written in the file (INF, WRN, ERR, FTL); empty = every level.
    public string? Level { get; init; }
}

public sealed record LogTailDto
{
    public string FileName { get; init; } = string.Empty;
    public DateTime LastWriteUtc { get; init; }
    public IReadOnlyList<string> Lines { get; init; } = Array.Empty<string>();
}
