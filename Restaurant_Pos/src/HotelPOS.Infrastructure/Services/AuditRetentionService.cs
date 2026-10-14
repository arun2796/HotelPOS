using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Contracts.Admin;
using HotelPOS.Domain.Administration;
using HotelPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HotelPOS.Infrastructure.Services;

// The only thing that ever removes audit rows: a server-side job driven by AuditRetentionDays (0 = keep all).
public sealed class AuditRetentionService : BackgroundService
{
    // The first run waits a little so it does not compete with start-up migrations and the first logins, but does
    // not wait a full interval: a server restarted every night would otherwise never purge.
    private static readonly TimeSpan FirstRunDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<AuditRetentionService> _logger;

    public AuditRetentionService(IServiceScopeFactory scopes, ILogger<AuditRetentionService> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(FirstRunDelay, stoppingToken);
            using var timer = new PeriodicTimer(Interval);
            do
            {
                await PurgeAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task PurgeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var days = await db.IntSettingAsync(SettingKeys.AuditRetentionDays, 0, cancellationToken);
            if (days <= 0)
            {
                return;
            }

            var cutoff = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow.AddDays(-days);
            var deleted = await db.AuditLogs.Where(a => a.Timestamp < cutoff).ExecuteDeleteAsync(cancellationToken);
            if (deleted == 0)
            {
                return;
            }

            // The purge itself stays on record, so a gap in the trail can always be explained.
            scope.ServiceProvider.GetRequiredService<IAuditService>().Record(
                AuditActions.AuditPurged, nameof(AuditLog), null,
                newValues: new { Deleted = deleted, OlderThanUtc = cutoff, RetentionDays = days },
                actor: new AuditActor(null, "system"));
            await db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Audit retention removed {Count} entries older than {Days} days", deleted, days);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Audit retention run failed; it will run again in six hours");
        }
    }
}
