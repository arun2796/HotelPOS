using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Auth;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Auth;
using HotelPOS.Desktop.Services.Configuration;
using HotelPOS.Desktop.Services.Navigation;

namespace HotelPOS.Desktop.Modules.Auth;

public enum ServerState
{
    Unknown,
    Checking,
    Reachable,
    Unreachable,
}

public sealed partial class LoginViewModel : ObservableObject, INavigationAware
{
    private readonly IAuthApi _authApi;
    private readonly ISystemApi _systemApi;
    private readonly IAuthSession _session;
    private readonly IClientSettingsService _settings;
    private readonly IUserPreferences _preferences;
    private readonly IAppNavigator _navigator;

    public LoginViewModel(
        IAuthApi authApi,
        ISystemApi systemApi,
        IAuthSession session,
        IClientSettingsService settings,
        IUserPreferences preferences,
        IAppNavigator navigator)
    {
        _authApi = authApi;
        _systemApi = systemApi;
        _session = session;
        _settings = settings;
        _preferences = preferences;
        _navigator = navigator;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private string _username = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private string _password = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _infoMessage;

    [ObservableProperty]
    private string _restaurantName = "HotelPOS";

    [ObservableProperty]
    private ServerState _serverState;

    public string ServerAddress => _settings.Current.ApiBaseUrl;

    public string TerminalDescription => $"{_settings.Current.DeviceName} · {_settings.Current.DeviceType}";

    public Task OnNavigatedToAsync(object? parameter)
    {
        InfoMessage = parameter as string;
        Username = _preferences.LastUsername ?? string.Empty;
        Password = string.Empty;
        OnPropertyChanged(nameof(ServerAddress));
        OnPropertyChanged(nameof(TerminalDescription));

        // Do not hold up the screen: the check runs in the background and updates the status line.
        _ = CheckServerAsync();
        return Task.CompletedTask;
    }

    public void OnNavigatedFrom() => Password = string.Empty;

    private bool CanLogin() => !IsBusy && !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrEmpty(Password);

    [RelayCommand(CanExecute = nameof(CanLogin))]
    private async Task LoginAsync()
    {
        ErrorMessage = null;
        InfoMessage = null;
        IsBusy = true;
        try
        {
            var settings = _settings.Current;
            var result = await _authApi.LoginAsync(new LoginRequest
            {
                Username = Username.Trim(),
                Password = Password,
                DeviceName = settings.DeviceName,
                DeviceType = settings.DeviceType,
                MachineName = Environment.MachineName,
                AppVersion = AppInfo.Version,
            });

            if (!result.Success || result.Data is null)
            {
                Password = string.Empty;
                ErrorMessage = result.IsConnectionFailure
                    ? $"Cannot reach the server at {settings.ApiBaseUrl}. Check the Wi-Fi/LAN connection, or the server address in Server settings."
                    : result.Message;
                ServerState = result.IsConnectionFailure ? ServerState.Unreachable : ServerState.Reachable;
                return;
            }

            var login = result.Data;
            _session.Start(login);
            _preferences.LastUsername = Username.Trim();
            if (login.DeviceId is { } deviceId && settings.DeviceId != deviceId)
            {
                settings.DeviceId = deviceId;
                _settings.Save(settings);
            }

            Password = string.Empty;
            if (login.User.MustChangePassword)
            {
                await _navigator.ShowChangePasswordAsync();
            }
            else
            {
                await _navigator.ShowShellAsync();
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CheckServerAsync()
    {
        ServerState = ServerState.Checking;
        var result = await _systemApi.GetInfoAsync();
        if (result.Success && result.Data is not null)
        {
            RestaurantName = result.Data.RestaurantName;
            ServerState = ServerState.Reachable;
        }
        else
        {
            ServerState = ServerState.Unreachable;
        }
    }

    [RelayCommand]
    private Task OpenServerSettingsAsync() => _navigator.ShowConfigurationAsync(isFirstRun: false);
}
