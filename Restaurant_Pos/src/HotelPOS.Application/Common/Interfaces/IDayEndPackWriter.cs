namespace HotelPOS.Application.Common.Interfaces;

public sealed record DayEndPackFile(string FileName, byte[] Content);

public sealed record DayEndPackLocation(string Folder, IReadOnlyList<string> Files);

// Writes the Day End report pack into a folder next to the backups (Infrastructure: the file system is an adapter).
public interface IDayEndPackWriter
{
    Task<DayEndPackLocation> WriteAsync(DateOnly businessDay, IReadOnlyList<DayEndPackFile> files, CancellationToken cancellationToken = default);

    // The same for a closed month or year (Phase 15): period-<label>-<time>, label such as 2026-09 or fy2026.
    Task<DayEndPackLocation> WritePeriodAsync(string label, IReadOnlyList<DayEndPackFile> files, CancellationToken cancellationToken = default);
}
