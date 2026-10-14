using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Admin;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Navigation;

namespace HotelPOS.Desktop.Modules.Auth;

public static class VersionGate
{
    // "1.4.0-beta+abc" -> 1.4.0. An unparsable version on either side never blocks: the server decides then.
    public static bool IsSupported(string clientVersion, string? minimumVersion) =>
        !TryParse(minimumVersion, out var minimum) || !TryParse(clientVersion, out var client) || client >= minimum;

    private static bool TryParse(string? text, out Version version)
    {
        var core = (text ?? string.Empty).Split('+', '-')[0].Trim();
        return Version.TryParse(core, out version!);
    }
}

// Shown instead of the login when the server needs a newer terminal (after a server upgrade). Signing in with an old
// client could send requests the new server no longer understands.
public sealed partial class UpdateRequiredViewModel : ObservableObject, INavigationAware
{
    private readonly ISystemApi _systemApi;
    private readonly IAppNavigator _navigator;

    public UpdateRequiredViewModel(ISystemApi systemApi, IAppNavigator navigator)
    {
        _systemApi = systemApi;
        _navigator = navigator;
    }

    public string InstalledVersion => AppInfo.Version;

    [ObservableProperty]
    private string _requiredVersion = string.Empty;

    [ObservableProperty]
    private string _serverVersion = string.Empty;

    [ObservableProperty]
    private string? _message;

    public Task OnNavigatedToAsync(object? parameter)
    {
        if (parameter is SystemInfoDto info)
        {
            RequiredVersion = info.MinClientVersion;
            ServerVersion = info.ApiVersion;
        }

        return Task.CompletedTask;
    }

    public void OnNavigatedFrom()
    {
    }

    [RelayCommand]
    private async Task CheckAgainAsync()
    {
        var result = await _systemApi.GetInfoAsync();
        if (result.Success && result.Data is not null && !VersionGate.IsSupported(AppInfo.Version, result.Data.MinClientVersion))
        {
            RequiredVersion = result.Data.MinClientVersion;
            Message = "This terminal still needs the update.";
            return;
        }

        await _navigator.ShowLoginAsync();
    }

    [RelayCommand]
    private Task OpenServerSettingsAsync() => _navigator.ShowConfigurationAsync(isFirstRun: false);
}
