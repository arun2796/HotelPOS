using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Admin;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Ui;
using HotelPOS.Desktop.Services.Updates;

namespace HotelPOS.Desktop.Modules.Auth;

public static class VersionGate
{
    // "1.4.0-beta+abc" -> 1.4.0. An unparsable version on either side never blocks: the server decides then.
    public static bool IsSupported(string clientVersion, string? minimumVersion) =>
        !TryParse(minimumVersion, out var minimum) || !TryParse(clientVersion, out var client) || client >= minimum;

    // True when candidate is a higher version than the installed one; an unparsable version is never "newer".
    public static bool IsNewer(string? candidate, string installedVersion) =>
        TryParse(candidate, out var latest) && TryParse(installedVersion, out var installed) && latest > installed;

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
    private readonly IUpdateService _updates;
    private readonly IAppLifetime _lifetime;

    public UpdateRequiredViewModel(ISystemApi systemApi, IAppNavigator navigator, IUpdateService updates, IAppLifetime lifetime)
    {
        _systemApi = systemApi;
        _navigator = navigator;
        _updates = updates;
        _lifetime = lifetime;
    }

    // The installer this server hosts, when it has one that is newer than this terminal.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUpdate))]
    private ClientUpdateInfo? _offer;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUpdate))]
    private bool _isUpdating;

    [ObservableProperty]
    private double _progress;

    public bool CanUpdate => Offer is not null && !IsUpdating;

    [RelayCommand]
    private async Task UpdateNowAsync()
    {
        if (Offer is not { } offer)
        {
            return;
        }

        IsUpdating = true;
        Message = null;
        try
        {
            var result = await _updates.DownloadAndStartAsync(offer, new Progress<double>(p => Progress = p * 100), CancellationToken.None);
            if (result.Success)
            {
                _lifetime.Exit();
                return;
            }

            Message = result.Message;
        }
        finally
        {
            IsUpdating = false;
        }
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
            Offer = _updates.OfferFor(info.ClientUpdate);
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
            Offer = _updates.OfferFor(result.Data.ClientUpdate);
            Message = "This terminal still needs the update.";
            return;
        }

        await _navigator.ShowLoginAsync();
    }

    [RelayCommand]
    private Task OpenServerSettingsAsync() => _navigator.ShowConfigurationAsync(isFirstRun: false);
}
