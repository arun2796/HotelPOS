using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Security;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Auth;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Ui;
using HotelPOS.Desktop.Services.Updates;
using Microsoft.Extensions.Logging;

namespace HotelPOS.Desktop.Shell;

// The two notes above every screen: the licence is in its trial or about to expire (or has expired: read-only), and
// the server hosts a newer HotelPOS than this terminal runs.
public sealed partial class StatusBannerViewModel : ObservableObject
{
    private readonly ILicenseApi _licenseApi;
    private readonly ISystemApi _systemApi;
    private readonly IUpdateService _updates;
    private readonly IAppLifetime _lifetime;
    private readonly IAuthSession _session;
    private readonly INavigationService _navigation;
    private readonly IDialogService _dialogs;
    private readonly ILogger<StatusBannerViewModel> _logger;

    public StatusBannerViewModel(
        ILicenseApi licenseApi,
        ISystemApi systemApi,
        IUpdateService updates,
        IAppLifetime lifetime,
        IAuthSession session,
        INavigationService navigation,
        IDialogService dialogs,
        ILogger<StatusBannerViewModel> logger)
    {
        _licenseApi = licenseApi;
        _systemApi = systemApi;
        _updates = updates;
        _lifetime = lifetime;
        _session = session;
        _navigation = navigation;
        _dialogs = dialogs;
        _logger = logger;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLicense))]
    private string _licenseText = string.Empty;

    [ObservableProperty]
    private bool _licenseIsCritical;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowUpdate), nameof(UpdateText))]
    private ClientUpdateInfo? _offer;

    [ObservableProperty]
    private bool _isUpdating;

    [ObservableProperty]
    private string? _updateError;

    public bool ShowLicense => !string.IsNullOrEmpty(LicenseText);

    public bool ShowUpdate => Offer is not null;

    public string UpdateText => Offer is null ? string.Empty : $"HotelPOS {Offer.Version} is available on the server (this terminal runs {AppInfo.Version}).";

    public bool CanOpenLicense => _session.User?.Roles.Contains(Roles.Admin) == true
        || _session.User?.Permissions.Contains(Permissions.LicenseManage) == true;

    public async Task RefreshAsync()
    {
        try
        {
            var license = await _licenseApi.GetStatusAsync();
            ApplyLicense(license.Success ? license.Data : null);

            var info = await _systemApi.GetInfoAsync();
            Offer = info.Success ? _updates.OfferFor(info.Data?.ClientUpdate) : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A banner never stops the terminal.
            _logger.LogWarning(ex, "Could not refresh the licence and update banner");
        }
    }

    public void ApplyLicense(LicenseStatusDto? status)
    {
        (LicenseText, LicenseIsCritical) = Describe(status);
        OnPropertyChanged(nameof(CanOpenLicense));
    }

    // The words of the licence banner; empty when nothing needs attention.
    public static (string Text, bool Critical) Describe(LicenseStatusDto? status)
    {
        if (status is null || !status.ShowWarning)
        {
            return (string.Empty, false);
        }

        if (status.IsExpired)
        {
            return ($"{status.Message ?? "The licence has expired."} HotelPOS is read-only until an administrator applies a licence key.", true);
        }

        var days = status.DaysRemaining ?? 0;
        var left = days == 1 ? "1 day" : $"{days} days";
        return status.State == LicenseStateNames.Trial
            ? ($"Trial version: {left} left. An administrator must apply a licence key before it ends.", false)
            : ($"The licence expires in {left}, on {status.ExpiresOn?.ToString("dd MMM yyyy", CultureInfo.CurrentCulture)}. Ask your supplier for a new key.", false);
    }

    [RelayCommand]
    private Task OpenLicenseAsync() => _navigation.NavigateToAsync(ModuleRegistry.License);

    [RelayCommand]
    private async Task UpdateNowAsync()
    {
        if (Offer is not { } offer || IsUpdating)
        {
            return;
        }

        if (!await _dialogs.ConfirmAsync(
                "Update this terminal?",
                $"HotelPOS will close, install version {offer.Version} and start again (about a minute). Orders already sent are safe on the server; finish what you are doing first.",
                "Update now"))
        {
            return;
        }

        IsUpdating = true;
        UpdateError = null;
        try
        {
            var result = await _updates.DownloadAndStartAsync(offer, null, CancellationToken.None);
            if (result.Success)
            {
                _lifetime.Exit();
                return;
            }

            UpdateError = result.Message;
        }
        finally
        {
            IsUpdating = false;
        }
    }
}
