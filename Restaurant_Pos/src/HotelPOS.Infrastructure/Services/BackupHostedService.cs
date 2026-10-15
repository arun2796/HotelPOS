using System.Globalization;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Enums;
using HotelPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HotelPOS.Infrastructure.Services;

// The daily backup at BackupTime (local). A server that was off at that time catches up when it starts. One
// scheduled attempt per day: a failure is reported (audit, Backup screen) instead of retried every minute.
public sealed class BackupHostedService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly BackupOptions _options;
    private readonly ILogger<BackupHostedService> _logger;

    public BackupHostedService(IServiceScopeFactory scopes, IOptions<BackupOptions> options, ILogger<BackupHostedService> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Scheduler)
        {
            return;
        }

        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RunIfDueAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Checking the backup schedule failed; it will be checked again in a minute");
            }
        }
    }

    public async Task RunIfDueAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        if (!await db.BoolSettingAsync(SettingKeys.BackupEnabled, true, cancellationToken))
        {
            return;
        }

        var dueUtc = DueTodayUtc(await db.SettingAsync(SettingKeys.BackupTime, cancellationToken), clock.UtcNow);
        if (clock.UtcNow < dueUtc
            || await db.Backups.AnyAsync(b => b.Trigger == BackupTrigger.Schedule && b.StartedAt >= dueUtc, cancellationToken))
        {
            return;
        }

        _logger.LogInformation("Starting the scheduled backup");
        await scope.ServiceProvider.GetRequiredService<IBackupService>().RunAsync(BackupTrigger.Schedule, cancellationToken);
    }

    internal static DateTime DueTodayUtc(string? backupTime, DateTime nowUtc)
    {
        var time = TimeOnly.TryParse(backupTime, CultureInfo.InvariantCulture, out var parsed) ? parsed : new TimeOnly(3, 30);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(nowUtc, TimeZoneInfo.Local));
        return TimeZoneInfo.ConvertTimeToUtc(today.ToDateTime(time, DateTimeKind.Unspecified), TimeZoneInfo.Local);
    }
}
