using System.Globalization;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Contracts.Admin;
using HotelPOS.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace HotelPOS.Infrastructure.Services;

// Each Day End run gets its own folder beside the backups (dayend-<day>-<time>), so a rerun after a reopen never
// overwrites the pack of the earlier close.
public sealed class DayEndPackWriter : IDayEndPackWriter
{
    private readonly AppDbContext _db;
    private readonly IClock _clock;
    private readonly BackupOptions _options;

    public DayEndPackWriter(AppDbContext db, IClock clock, IOptions<BackupOptions> options)
    {
        _db = db;
        _clock = clock;
        _options = options.Value;
    }

    public async Task<DayEndPackLocation> WriteAsync(DateOnly businessDay, IReadOnlyList<DayEndPackFile> files, CancellationToken cancellationToken = default) =>
        await WriteFolderAsync("dayend-" + businessDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), files, cancellationToken);

    public async Task<DayEndPackLocation> WritePeriodAsync(string label, IReadOnlyList<DayEndPackFile> files, CancellationToken cancellationToken = default) =>
        await WriteFolderAsync("period-" + label, files, cancellationToken);

    private async Task<DayEndPackLocation> WriteFolderAsync(string name, IReadOnlyList<DayEndPackFile> files, CancellationToken cancellationToken)
    {
        var configured = await _db.SettingAsync(SettingKeys.BackupPath, cancellationToken);
        var root = string.IsNullOrWhiteSpace(configured) ? _options.DefaultPath : configured.Trim();
        var stamp = TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow, TimeZoneInfo.Local).ToString("HHmmss", CultureInfo.InvariantCulture);
        var folder = Path.Combine(root, $"{name}-{stamp}");
        Directory.CreateDirectory(folder);
        foreach (var file in files)
        {
            await File.WriteAllBytesAsync(Path.Combine(folder, Path.GetFileName(file.FileName)), file.Content, cancellationToken);
        }

        return new DayEndPackLocation(folder, files.Select(f => Path.GetFileName(f.FileName)).ToList());
    }
}
