using HotelPOS.Contracts.Admin;
using HotelPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HotelPOS.Infrastructure.Services;

// The only thing that ever removes audit rows: a server-side job driven by AuditRetentionDays (0 = keep all).
public sealed class AuditRetentionService : BackgroundService
{
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
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var text = await db.Settings.AsNoTracking().Where(s => s.Key == SettingKeys.AuditRetentionDays).Select(s => s.Value).FirstOrDefaultAsync(stoppingToken);
                if (!int.TryParse(text, out var days) || days <= 0)
                {
                    continue;
                }

                var cutoff = DateTime.UtcNow.AddDays(-days);
                var deleted = await db.AuditLogs.Where(a => a.Timestamp < cutoff).ExecuteDeleteAsync(stoppingToken);
                if (deleted > 0)
                {
                    _logger.LogInformation("Audit retention removed {Count} entries older than {Days} days", deleted, days);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Audit retention run failed; it will run again in six hours");
            }
        }
    }
}
