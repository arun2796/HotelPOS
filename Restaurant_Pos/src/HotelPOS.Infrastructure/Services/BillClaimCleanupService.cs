using HotelPOS.Application.Billing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HotelPOS.Infrastructure.Services;

// Expired claims are already ignored by every check; clearing them lets the queues drop the "being handled" badge.
public sealed class BillClaimCleanupService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<BillClaimCleanupService> _logger;

    public BillClaimCleanupService(IServiceScopeFactory scopes, ILogger<BillClaimCleanupService> logger)
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
                var released = await scope.ServiceProvider.GetRequiredService<IBillingService>().ReleaseExpiredClaimsAsync(stoppingToken);
                if (released > 0)
                {
                    _logger.LogInformation("Released {Count} expired bill claims", released);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Releasing expired bill claims failed; it will run again in a minute");
            }
        }
    }
}
