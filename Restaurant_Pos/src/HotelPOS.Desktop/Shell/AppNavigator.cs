using HotelPOS.Desktop.Modules.Auth;
using HotelPOS.Desktop.Modules.Config;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Auth;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Realtime;
using HotelPOS.Desktop.Services.Ui;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HotelPOS.Desktop.Shell;

/// <summary>Switches the window between configuration, login, password change and the main shell.</summary>
public sealed class AppNavigator : IAppNavigator
{
    private readonly MainViewModel _main;
    private readonly IServiceProvider _services;
    private readonly IAuthSession _session;
    private readonly IRealtimeClient _realtime;
    private readonly IAuthApi _authApi;
    private readonly ILogger<AppNavigator> _logger;

    public AppNavigator(
        MainViewModel main,
        IServiceProvider services,
        IAuthSession session,
        IRealtimeClient realtime,
        IAuthApi authApi,
        IUiDispatcher dispatcher,
        ILogger<AppNavigator> logger)
    {
        _main = main;
        _services = services;
        _session = session;
        _realtime = realtime;
        _authApi = authApi;
        _logger = logger;

        // The server refused to renew the session (user disabled, password reset, token revoked...).
        _session.Expired += (_, reason) => dispatcher.Post(() => _ = LogoutAsync(reason));
    }

    public Task ShowConfigurationAsync(bool isFirstRun) =>
        ShowAsync(ActivatorUtilities.CreateInstance<ConfigurationViewModel>(_services), new ConfigurationContext(isFirstRun, InShell: false));

    public Task ShowLoginAsync(string? message = null) =>
        ShowAsync(ActivatorUtilities.CreateInstance<LoginViewModel>(_services), message);

    public Task ShowChangePasswordAsync() =>
        ShowAsync(ActivatorUtilities.CreateInstance<ChangePasswordViewModel>(_services), ChangePasswordMode.Forced);

    public Task ShowShellAsync() =>
        ShowAsync(ActivatorUtilities.CreateInstance<ShellViewModel>(_services), null);

    public async Task LogoutAsync(string? message = null)
    {
        var refreshToken = _session.RefreshToken;
        var user = _session.User?.Username;

        await _realtime.StopAsync();
        _session.Clear();
        await ShowLoginAsync(message);

        if (refreshToken is not null)
        {
            // Best effort: revoke the refresh token on the server; the local copy is already gone.
            _ = Task.Run(async () =>
            {
                var result = await _authApi.LogoutAsync(refreshToken);
                if (!result.Success)
                {
                    _logger.LogInformation("Server-side logout not confirmed: {Message}", result.Message);
                }
            });
        }

        _logger.LogInformation("User {User} signed out{Reason}", user, message is null ? string.Empty : $" ({message})");
    }

    private async Task ShowAsync(object screen, object? parameter)
    {
        var previous = _main.CurrentScreen;
        if (previous is INavigationAware leaving)
        {
            leaving.OnNavigatedFrom();
        }

        if (previous is IDisposable disposable && !ReferenceEquals(previous, screen))
        {
            disposable.Dispose();
        }

        _main.CurrentScreen = screen;
        if (screen is INavigationAware arriving)
        {
            await arriving.OnNavigatedToAsync(parameter);
        }
    }
}
