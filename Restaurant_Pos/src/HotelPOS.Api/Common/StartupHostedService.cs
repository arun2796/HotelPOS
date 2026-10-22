using HotelPOS.Infrastructure.Persistence;

namespace HotelPOS.Api.Common;

// The database start-up (wait for PostgreSQL, pre-upgrade backup, migrations, seed, required-settings check) as the
// FIRST hosted service rather than top-level code before app.RunAsync(). Under the Service Control Manager the host
// reports "starting" only inside RunAsync, and Windows gives a service 30 s to do so; the database wait alone may
// take 120 s after a power cut, which used to end in error 1053 ("did not respond in a timely fashion") and a failed
// start. Here the SCM is already answered, Kestrel (registered last by WebApplicationBuilder) is not listening yet,
// and a failure is logged with its reason before the process exits 1 so the recovery actions restart it.
public sealed class StartupHostedService : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<StartupHostedService> _logger;
    private bool _done;

    public StartupHostedService(IServiceProvider services, IHostEnvironment environment, ILogger<StartupHostedService> logger)
    {
        _services = services;
        _environment = environment;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_done)
        {
            return;
        }

        try
        {
            await _services.InitializeDatabaseAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Logged here, while the logger is still alive: the host disposes it on the way out of RunAsync.
            _logger.LogCritical(ex, "HotelPOS API cannot start: {Reason}", ex.Message);
            throw;
        }

        StartupDiagnostics.WarnAboutLowDiskSpace(_services, _logger);
        _logger.LogInformation("HotelPOS API {Version} starting ({Environment})", ApiServiceRegistration.ApiVersion, _environment.EnvironmentName);
        _done = true;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
