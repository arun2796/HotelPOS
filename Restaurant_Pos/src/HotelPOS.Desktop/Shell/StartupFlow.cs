using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Auth;
using HotelPOS.Desktop.Services.Configuration;
using HotelPOS.Desktop.Services.Navigation;
using Microsoft.Extensions.Logging;

namespace HotelPOS.Desktop.Shell;

public sealed class StartupFlow
{
    private readonly IClientSettingsService _settings;
    private readonly IAuthSession _session;
    private readonly IAuthApi _authApi;
    private readonly IAppNavigator _navigator;
    private readonly ILogger<StartupFlow> _logger;

    public StartupFlow(IClientSettingsService settings, IAuthSession session, IAuthApi authApi, IAppNavigator navigator, ILogger<StartupFlow> logger)
    {
        _settings = settings;
        _session = session;
        _authApi = authApi;
        _navigator = navigator;
        _logger = logger;
    }

    public async Task RunAsync()
    {
        if (!_settings.Current.IsConfigured)
        {
            _logger.LogInformation("No server configured on this terminal; showing first-run configuration");
            await _navigator.ShowConfigurationAsync(isFirstRun: true);
            return;
        }

        // Resume the previous session (e.g. after a crash or power cut) if the server still accepts it.
        var refreshToken = _session.LoadPersistedRefreshToken();
        if (refreshToken is null)
        {
            await _navigator.ShowLoginAsync();
            return;
        }

        var result = await _authApi.RefreshAsync(refreshToken);
        if (result.Success && result.Data is not null)
        {
            _session.Start(result.Data);
            _logger.LogInformation("Resumed session of {User}", result.Data.User.Username);
            if (result.Data.User.MustChangePassword)
            {
                await _navigator.ShowChangePasswordAsync();
            }
            else
            {
                await _navigator.ShowShellAsync();
            }

            return;
        }

        if (!result.IsConnectionFailure)
        {
            _session.Clear();
        }

        await _navigator.ShowLoginAsync(result.IsConnectionFailure ? null : "Your previous session has ended. Please sign in.");
    }
}
