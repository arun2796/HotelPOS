using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Admin;
using HotelPOS.Desktop.Modules.Common;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Ui;
using HotelPOS.Desktop.Shell;

namespace HotelPOS.Desktop.Modules.Licensing;

// The server's licence: state, edition, terminals, expiry, the machine id to quote when ordering a key, and where
// to paste the key (administrators).
public sealed partial class LicenseViewModel : ViewModelBase, INavigationAware, IRefreshable
{
    private readonly ILicenseApi _api;
    private readonly INotificationService _notifications;
    private readonly StatusBannerViewModel? _banner;

    public LicenseViewModel(ILicenseApi api, INotificationService notifications, StatusBannerViewModel? banner = null)
    {
        _api = api;
        _notifications = notifications;
        _banner = banner;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus), nameof(StateText), nameof(TerminalsText), nameof(ExpiryText), nameof(IsProblem), nameof(MachineId), nameof(EditionText), nameof(CustomerText))]
    private LicenseStatusDto? _status;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyKeyCommand))]
    private string _keyText = string.Empty;

    public bool HasStatus => Status is not null;

    public string MachineId => Status?.MachineId ?? string.Empty;

    public bool IsProblem => Status?.IsExpired == true;

    public string StateText => Status?.State switch
    {
        LicenseStateNames.Licensed => "Licensed",
        LicenseStateNames.Expired => "Expired: read-only",
        LicenseStateNames.Trial => Status.DaysRemaining is { } days ? $"Trial: {days} day(s) left" : "Trial",
        _ => string.Empty,
    };

    public string EditionText => Status?.Edition ?? string.Empty;

    public string CustomerText => Status?.Customer ?? "Not licensed to a customer yet";

    public string TerminalsText => Status is null ? string.Empty : $"{Status.TerminalsInUse} registered of {Status.MaxTerminals} licensed";

    public string ExpiryText => Status switch
    {
        null => string.Empty,
        { ExpiresOn: { } on } => on.ToString("dd MMM yyyy", CultureInfo.CurrentCulture),
        _ => "Never (perpetual licence)",
    };

    public Task OnNavigatedToAsync(object? parameter) => RefreshAsync();

    public void OnNavigatedFrom()
    {
    }

    public async Task RefreshAsync()
    {
        if (await FetchAsync(_api.GetStatusAsync()) is { } status)
        {
            ErrorMessage = null;
            Status = status;
        }
    }

    private bool CanApply() => !string.IsNullOrWhiteSpace(KeyText);

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyKeyAsync()
    {
        await ExecuteAsync(async () =>
        {
            var result = await _api.ApplyAsync(KeyText.Trim());
            if (!result.Success || result.Data is null)
            {
                ErrorMessage = ApiFailures.Describe(result);
                return;
            }

            Status = result.Data;
            KeyText = string.Empty;
            _banner?.ApplyLicense(result.Data);
            _notifications.Success($"Licence applied: {result.Data.Edition}, {result.Data.MaxTerminals} terminal(s).");
        });
    }

    [RelayCommand]
    private void CopyMachineId()
    {
        try
        {
            System.Windows.Clipboard.SetText(MachineId);
            _notifications.Info("Machine id copied.");
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            _notifications.Warning("The clipboard is busy; select the machine id and copy it by hand.");
        }
    }
}
