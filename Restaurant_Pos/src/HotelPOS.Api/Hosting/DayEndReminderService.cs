using HotelPOS.Application.Features.DayEnd;

namespace HotelPOS.Api.Hosting;

// Checks once a minute whether the day-end reminder is due; DayEndReminder decides and remembers it was sent.
public sealed class DayEndReminderService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<DayEndReminderService> _logger;

    public DayEndReminderService(IServiceScopeFactory scopes, ILogger<DayEndReminderService> logger)
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
                if (await scope.ServiceProvider.GetRequiredService<DayEndReminder>().RunAsync(stoppingToken))
                {
                    _logger.LogInformation("Day-end reminder sent to managers");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "The day-end reminder check failed; it will run again in a minute");
            }
        }
    }
}
