using HotelPOS.Infrastructure.Media;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace HotelPOS.Api.Common;

public sealed class MonitoringOptions
{
    public const string SectionName = "Monitoring";

    // Below this much free space on a drive holding logs, pictures or backups the server reports Degraded.
    public long MinFreeDiskMb { get; set; } = 1024;
}

// One definition of the folders the server writes to, shared by Serilog, the log tail endpoint and the disk check.
public static class ServerPaths
{
    public static string LogDirectory(IConfiguration configuration, IHostEnvironment environment)
    {
        var configured = configuration["Logging:Directory"];
        return string.IsNullOrWhiteSpace(configured) ? Path.Combine(environment.ContentRootPath, "logs") : configured;
    }

    public static IReadOnlyList<string> Written(IServiceProvider services)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var environment = services.GetRequiredService<IHostEnvironment>();
        return new[]
            {
                LogDirectory(configuration, environment),
                services.GetRequiredService<IOptions<MediaOptions>>().Value.MenuImagesPath,
            }
            .Concat(services.GetServices<IExtraMonitoredPath>().Select(p => p.Path))
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();
    }
}

// Lets later features (backups) add their folder to the disk check without this file knowing about them.
public interface IExtraMonitoredPath
{
    string Path { get; }
}

// The backup folder comes from a database setting; the backup service records the one it last used.
public sealed class BackupFolderPath : IExtraMonitoredPath
{
    private readonly IOptions<Infrastructure.Services.BackupOptions> _options;

    public BackupFolderPath(IOptions<Infrastructure.Services.BackupOptions> options)
    {
        _options = options;
    }

    public string Path => Infrastructure.Services.BackupFolder.Current ?? _options.Value.DefaultPath;
}

public sealed record DriveSpace(string Drive, long FreeMb);

public static class DiskSpace
{
    public static IReadOnlyList<DriveSpace> For(IEnumerable<string> paths) =>
        paths.Select(p => Path.GetPathRoot(Path.GetFullPath(p)))
            .Where(root => !string.IsNullOrEmpty(root))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(root => new DriveInfo(root!))
            .Where(drive => drive.IsReady)
            .Select(drive => new DriveSpace(drive.Name, drive.AvailableFreeSpace / (1024 * 1024)))
            .ToList();
}

public sealed class DiskSpaceHealthCheck : IHealthCheck
{
    private readonly IServiceProvider _services;
    private readonly IOptionsMonitor<MonitoringOptions> _options;

    public DiskSpaceHealthCheck(IServiceProvider services, IOptionsMonitor<MonitoringOptions> options)
    {
        _services = services;
        _options = options;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var minimum = _options.CurrentValue.MinFreeDiskMb;
        var drives = DiskSpace.For(ServerPaths.Written(_services));
        var data = drives.ToDictionary(d => d.Drive, d => (object)$"{d.FreeMb} MB free");
        var low = drives.Where(d => d.FreeMb < minimum).ToList();

        // Degraded, not Unhealthy: the API still serves orders, but logs, pictures and backups are about to fail.
        return Task.FromResult(low.Count == 0
            ? HealthCheckResult.Healthy("Enough free disk space.", data)
            : HealthCheckResult.Degraded(
                $"Low disk space on {string.Join(", ", low.Select(d => $"{d.Drive} ({d.FreeMb} MB)"))}; at least {minimum} MB is expected.",
                data: data));
    }
}

public static class StartupDiagnostics
{
    public static void WarnAboutLowDiskSpace(IServiceProvider services, ILogger logger)
    {
        var minimum = services.GetRequiredService<IOptions<MonitoringOptions>>().Value.MinFreeDiskMb;
        foreach (var drive in DiskSpace.For(ServerPaths.Written(services)).Where(d => d.FreeMb < minimum))
        {
            logger.LogWarning("Low disk space on {Drive}: {FreeMb} MB free (minimum {MinimumMb} MB)", drive.Drive, drive.FreeMb, minimum);
        }
    }
}
