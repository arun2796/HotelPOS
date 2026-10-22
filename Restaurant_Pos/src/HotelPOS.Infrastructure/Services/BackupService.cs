using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Enums;
using HotelPOS.Domain.Administration;
using HotelPOS.Infrastructure.Media;
using HotelPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace HotelPOS.Infrastructure.Services;

public sealed class BackupOptions
{
    public const string SectionName = "Backup";

    // Used when the BackupPath setting is empty.
    public string DefaultPath { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HotelPOS", "Backups");

    // Folder holding pg_dump.exe / pg_restore.exe; empty searches PATH and then Program Files\PostgreSQL\<version>\bin.
    public string? PgBinPath { get; set; }

    public int TimeoutMinutes { get; set; } = 30;

    // Off in test hosts, which must not dump their throw-away databases on the daily schedule.
    public bool Scheduler { get; set; } = true;
}

public sealed class BackupService : IBackupService
{
    // One backup at a time across the scheduler and every "Backup now" click.
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly BackupOptions _options;
    private readonly MediaOptions _media;
    private readonly ILogger<BackupService> _logger;

    public BackupService(
        AppDbContext db,
        IAuditService audit,
        ICurrentUser currentUser,
        IClock clock,
        IOptions<BackupOptions> options,
        IOptions<MediaOptions> media,
        ILogger<BackupService> logger)
    {
        _db = db;
        _audit = audit;
        _currentUser = currentUser;
        _clock = clock;
        _options = options.Value;
        _media = media.Value;
        _logger = logger;
    }

    public async Task<Result<BackupDto>> RunAsync(BackupTrigger trigger, CancellationToken cancellationToken = default)
    {
        if (!await Gate.WaitAsync(TimeSpan.Zero, cancellationToken))
        {
            return AppErrors.BusinessRule("A backup is already running. Wait for it to finish.");
        }

        try
        {
            return await RunExclusiveAsync(trigger, cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// The safety copy taken before database migrations are applied at start-up (docs/ops/04 § 2): a verified
    /// pg_dump of the current schema and data in the backup folder, named after the upgrade. No media zip and no
    /// Backups row: the schema is about to change, so nothing is written to the database until the migration is done.
    /// Throws with the reason when pg_dump or pg_restore fail.
    /// </summary>
    public async Task<string> DumpBeforeUpgradeAsync(string label, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var folder = await FolderAsync(cancellationToken);
            Directory.CreateDirectory(folder);
            var stamp = TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow, TimeZoneInfo.Local).ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var safeLabel = new string(label.Where(char.IsLetterOrDigit).Take(40).ToArray());
            var dumpPath = Path.Combine(folder, $"hotelpos-{stamp}-before-{safeLabel}.dump");
            try
            {
                await DumpAsync(dumpPath, cancellationToken);
                await VerifyAsync(dumpPath, cancellationToken);
            }
            catch
            {
                DeleteQuietly(dumpPath);
                throw;
            }

            _logger.LogInformation("Pre-upgrade backup {File} written to {Folder}", Path.GetFileName(dumpPath), folder);
            return dumpPath;
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<BackupOverviewDto> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        var backups = await _db.Backups.AsNoTracking().OrderByDescending(b => b.StartedAt).Take(100).ToListAsync(cancellationToken);
        var lastSuccess = backups.FirstOrDefault(b => b.Status == BackupStatus.Succeeded);
        var lastFailure = backups.FirstOrDefault(b => b.Status == BackupStatus.Failed);
        return new BackupOverviewDto
        {
            Folder = await FolderAsync(cancellationToken),
            Enabled = await _db.BoolSettingAsync(Contracts.Admin.SettingKeys.BackupEnabled, true, cancellationToken),
            Time = await _db.SettingAsync(Contracts.Admin.SettingKeys.BackupTime, cancellationToken) ?? "03:30",
            RetentionDays = await RetentionDaysAsync(cancellationToken),
            // A failure older than the last success is history, not something to warn about.
            LastFailure = lastFailure is not null && (lastSuccess is null || lastFailure.StartedAt > lastSuccess.StartedAt) ? ToDto(lastFailure) : null,
            Backups = backups.Select(ToDto).ToList(),
        };
    }

    private async Task<Result<BackupDto>> RunExclusiveAsync(BackupTrigger trigger, CancellationToken cancellationToken)
    {
        // Holding the gate means nothing else is running: a row still marked Running was cut off by a server stop.
        foreach (var interrupted in await _db.Backups.Where(b => b.Status == BackupStatus.Running).ToListAsync(cancellationToken))
        {
            interrupted.Fail("Interrupted: the server stopped during the backup.", _clock.UtcNow);
        }

        var folder = await FolderAsync(cancellationToken);
        var stamp = TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow, TimeZoneInfo.Local).ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var backup = new Backup($"hotelpos-{stamp}.dump", trigger, trigger == BackupTrigger.Schedule ? "schedule" : _currentUser.UserName, _clock.UtcNow);
        _db.Backups.Add(backup);
        await _db.SaveChangesAsync(cancellationToken);

        var dumpPath = Path.Combine(folder, backup.FileName);
        var mediaPath = Path.Combine(folder, Path.ChangeExtension(backup.FileName, null) + "-media.zip");
        try
        {
            Directory.CreateDirectory(folder);
            await DumpAsync(dumpPath, cancellationToken);
            await VerifyAsync(dumpPath, cancellationToken);
            var mediaFile = ZipMedia(mediaPath);

            var size = new FileInfo(dumpPath).Length + (mediaFile is null ? 0 : new FileInfo(mediaPath).Length);
            backup.Succeed(size, mediaFile, _clock.UtcNow);
            _audit.Record(AuditActions.BackupCompleted, nameof(Backup), backup.Id.ToString(CultureInfo.InvariantCulture),
                newValues: new { backup.FileName, backup.SizeBytes, Trigger = trigger.ToString(), Folder = folder });
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Backup {File} written to {Folder} ({Size:N0} bytes)", backup.FileName, folder, size);

            await ApplyRetentionAsync(folder, cancellationToken);
            return ToDto(backup);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            DeleteQuietly(dumpPath);
            DeleteQuietly(mediaPath);
            backup.Fail(ex.Message, _clock.UtcNow);
            _audit.Record(AuditActions.BackupFailed, nameof(Backup), backup.Id.ToString(CultureInfo.InvariantCulture),
                newValues: new { backup.FileName, Trigger = trigger.ToString(), Folder = folder, Error = ex.Message });
            await _db.SaveChangesAsync(CancellationToken.None);
            _logger.LogError(ex, "Backup {File} failed", backup.FileName);
            return AppErrors.BusinessRule($"The backup failed: {ex.Message}");
        }
    }

    private async Task DumpAsync(string dumpPath, CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnectionStringBuilder(_db.Database.GetConnectionString());
        await RunToolAsync("pg_dump", connection, cancellationToken,
            "--format=custom", "--no-owner", "--file", dumpPath,
            "--host", connection.Host ?? "localhost", "--port", connection.Port.ToString(CultureInfo.InvariantCulture),
            "--username", connection.Username ?? string.Empty, "--dbname", connection.Database ?? string.Empty);
    }

    // A dump that pg_restore can list is readable; an empty table of contents means something went wrong.
    private async Task VerifyAsync(string dumpPath, CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnectionStringBuilder(_db.Database.GetConnectionString());
        var listing = await RunToolAsync("pg_restore", connection, cancellationToken, "--list", dumpPath);
        if (!listing.Contains("TABLE DATA", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The backup file could not be verified: pg_restore found no table data in it.");
        }
    }

    private string? ZipMedia(string zipPath)
    {
        var source = _media.MenuImagesPath;
        if (!Directory.Exists(source) || !Directory.EnumerateFiles(source).Any())
        {
            return null;
        }

        ZipFile.CreateFromDirectory(source, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
        return Path.GetFileName(zipPath);
    }

    private async Task ApplyRetentionAsync(string folder, CancellationToken cancellationToken)
    {
        var days = await RetentionDaysAsync(cancellationToken);
        var cutoff = _clock.UtcNow.AddDays(-days);
        var expired = await _db.Backups.Where(b => b.StartedAt < cutoff && b.Status != BackupStatus.Running).ToListAsync(cancellationToken);
        foreach (var old in expired)
        {
            DeleteQuietly(Path.Combine(folder, old.FileName));
            if (old.MediaFileName is not null)
            {
                DeleteQuietly(Path.Combine(folder, old.MediaFileName));
            }
        }

        _db.Backups.RemoveRange(expired);
        await _db.SaveChangesAsync(cancellationToken);
        if (expired.Count > 0)
        {
            _logger.LogInformation("Removed {Count} backups older than {Days} days", expired.Count, days);
        }
    }

    private async Task<string> RunToolAsync(string tool, NpgsqlConnectionStringBuilder connection, CancellationToken cancellationToken, params string[] arguments)
    {
        var executable = PostgresTools.Find(tool, _options.PgBinPath)
            ?? throw new InvalidOperationException(
                $"{tool} was not found. Install the PostgreSQL client tools or set Backup:PgBinPath to their bin folder.");

        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        // The password goes through the environment, never on the command line where other processes can read it.
        if (!string.IsNullOrEmpty(connection.Password))
        {
            start.Environment["PGPASSWORD"] = connection.Password;
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{tool} could not be started.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(_options.TimeoutMinutes));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        if (process.ExitCode != 0)
        {
            var message = (await error).Trim();
            throw new InvalidOperationException($"{tool} exited with code {process.ExitCode}: {(message.Length > 0 ? message : "no details")}");
        }

        return await output;
    }

    private async Task<string> FolderAsync(CancellationToken cancellationToken)
    {
        var configured = await _db.SettingAsync(Contracts.Admin.SettingKeys.BackupPath, cancellationToken);
        var folder = string.IsNullOrWhiteSpace(configured) ? _options.DefaultPath : configured.Trim();
        BackupFolder.Current = folder;
        return folder;
    }

    private async Task<int> RetentionDaysAsync(CancellationToken cancellationToken) =>
        Math.Max(1, await _db.IntSettingAsync(Contracts.Admin.SettingKeys.BackupRetentionDays, 14, cancellationToken));

    private void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Could not delete {File}", path);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Could not delete {File}", path);
        }
    }

    internal static BackupDto ToDto(Backup backup) => new()
    {
        Id = backup.Id,
        FileName = backup.FileName,
        MediaFileName = backup.MediaFileName,
        SizeBytes = backup.SizeBytes,
        Status = backup.Status,
        Trigger = backup.Trigger,
        TriggeredBy = backup.TriggeredBy,
        StartedAtUtc = backup.StartedAt,
        FinishedAtUtc = backup.FinishedAt,
        Error = backup.Error,
    };
}

// The folder of the latest backup, for the API's free-disk check (which cannot query settings synchronously).
public static class BackupFolder
{
    public static string? Current { get; set; }
}

public static class PostgresTools
{
    public static string? Find(string tool, string? configuredBin)
    {
        var file = tool + (OperatingSystem.IsWindows() ? ".exe" : string.Empty);
        if (!string.IsNullOrWhiteSpace(configuredBin))
        {
            var configured = Path.Combine(configuredBin, file);
            return File.Exists(configured) ? configured : null;
        }

        var onPath = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(dir => Path.Combine(dir.Trim(), file))
            .FirstOrDefault(File.Exists);
        if (onPath is not null)
        {
            return onPath;
        }

        // The standard installer puts each major version in its own folder; prefer the newest.
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PostgreSQL");
        return Directory.Exists(root)
            ? Directory.EnumerateDirectories(root)
                .Select(dir => (Version: int.TryParse(Path.GetFileName(dir), out var v) ? v : 0, Path: Path.Combine(dir, "bin", file)))
                .Where(c => File.Exists(c.Path))
                .OrderByDescending(c => c.Version)
                .Select(c => c.Path)
                .FirstOrDefault()
            : null;
    }
}
