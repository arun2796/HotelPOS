using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Devices;
using HotelPOS.Contracts.Enums;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Configuration;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Modules.Config;

/// <param name="IsFirstRun">No configuration exists yet; there is nowhere to go back to.</param>
/// <param name="InShell">Opened from the main shell by a logged-in manager/admin.</param>
public sealed record ConfigurationContext(bool IsFirstRun, bool InShell);

/// <summary>
/// Server address and device identity of this terminal. Used as the first-run screen, from the login
/// screen ("Server settings") and inside the shell ("This Terminal").
/// </summary>
public sealed partial class ConfigurationViewModel : ObservableObject, INavigationAware
{
    private readonly IClientSettingsService _settings;
    private readonly ISystemApi _systemApi;
    private readonly IAppNavigator _navigator;
    private readonly IDialogService _dialogs;

    public ConfigurationViewModel(IClientSettingsService settings, ISystemApi systemApi, IAppNavigator navigator, IDialogService dialogs)
    {
        _settings = settings;
        _systemApi = systemApi;
        _navigator = navigator;
        _dialogs = dialogs;
    }

    public IReadOnlyList<DeviceType> DeviceTypes { get; } = Enum.GetValues<DeviceType>();

    [ObservableProperty]
    private ConfigurationContext _context = new(IsFirstRun: false, InShell: false);

    [ObservableProperty]
    private string _apiBaseUrl = string.Empty;

    [ObservableProperty]
    private string _deviceName = string.Empty;

    [ObservableProperty]
    private DeviceType _deviceType = DeviceType.Waiter;

    [ObservableProperty]
    private string? _validationMessage;

    [ObservableProperty]
    private string? _testResult;

    [ObservableProperty]
    private bool _testSucceeded;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(TestConnectionCommand))]
    private bool _isBusy;

    public string Title => Context.IsFirstRun ? "Connect this terminal" : "This terminal";

    public string Subtitle => Context.IsFirstRun
        ? "Enter the address of the HotelPOS server on your restaurant network and name this terminal."
        : "Changing the server or the terminal name signs you out.";

    public bool CanCancel => !Context.IsFirstRun && !Context.InShell;

    public string? ConfigFilePath => _settings.FilePath;

    public Task OnNavigatedToAsync(object? parameter)
    {
        Context = parameter as ConfigurationContext ?? new ConfigurationContext(IsFirstRun: false, InShell: true);
        var current = _settings.Current;
        ApiBaseUrl = current.ApiBaseUrl;
        DeviceName = current.DeviceName;
        DeviceType = current.DeviceType;
        if (string.IsNullOrWhiteSpace(DeviceName))
        {
            DeviceName = SuggestDeviceName(DeviceType);
        }

        return Task.CompletedTask;
    }

    public void OnNavigatedFrom()
    {
    }

    /// <summary>Returns an error message, or null when the values are valid.</summary>
    public static string? Validate(string apiBaseUrl, string deviceName, out string normalizedUrl)
    {
        if (!ApiUrl.TryNormalize(apiBaseUrl, out normalizedUrl, out var urlError))
        {
            return urlError;
        }

        var name = deviceName?.Trim() ?? string.Empty;
        if (name.Length < DeviceNameRules.MinLength || name.Length > DeviceNameRules.MaxLength)
        {
            return $"The terminal name must be {DeviceNameRules.MinLength} to {DeviceNameRules.MaxLength} characters, e.g. WAITER-01.";
        }

        return Regex.IsMatch(name, DeviceNameRules.Pattern)
            ? null
            : "The terminal name may contain letters, digits, '-' and '_' only, e.g. WAITER-01.";
    }

    partial void OnContextChanged(ConfigurationContext value)
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(CanCancel));
    }

    partial void OnDeviceTypeChanged(DeviceType oldValue, DeviceType newValue)
    {
        // Keep the suggested name in step with the type until the technician types their own.
        if (string.IsNullOrWhiteSpace(DeviceName) || DeviceName == SuggestDeviceName(oldValue))
        {
            DeviceName = SuggestDeviceName(newValue);
        }
    }

    private static string SuggestDeviceName(DeviceType type) => type switch
    {
        DeviceType.Kitchen => "KITCHEN-01",
        DeviceType.Billing => "BILLING-01",
        DeviceType.Admin => "ADMIN-01",
        _ => "WAITER-01",
    };

    private bool CanRun() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task TestConnectionAsync()
    {
        TestResult = null;
        if (!ApiUrl.TryNormalize(ApiBaseUrl, out var url, out var error))
        {
            TestSucceeded = false;
            TestResult = error;
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _systemApi.GetInfoAsync(url);
            TestSucceeded = result.Success;
            TestResult = result.Success && result.Data is not null
                ? $"Connected to \"{result.Data.RestaurantName}\" (server version {result.Data.ApiVersion})."
                : result.IsConnectionFailure
                    ? $"No HotelPOS server answered at {url}. Check the address, that the server is running and that the firewall allows the port."
                    : result.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task SaveAsync()
    {
        ValidationMessage = Validate(ApiBaseUrl, DeviceName, out var url);
        if (ValidationMessage is not null)
        {
            return;
        }

        var current = _settings.Current;
        var name = DeviceName.Trim().ToUpperInvariant();
        var changed = !string.Equals(current.ApiBaseUrl, url, StringComparison.OrdinalIgnoreCase)
            || current.DeviceName != name
            || current.DeviceType != DeviceType;

        if (Context.InShell && changed && !await _dialogs.ConfirmAsync(
                "Save terminal settings",
                "Saving signs you out so this terminal can reconnect with the new settings. Continue?",
                "Save and sign out"))
        {
            return;
        }

        var updated = current.Clone();
        updated.ApiBaseUrl = url;
        updated.DeviceType = DeviceType;
        if (updated.DeviceName != name)
        {
            // A new name is a new device identity; the server assigns a new id at the next login.
            updated.DeviceName = name;
            updated.DeviceId = null;
        }

        _settings.Save(updated);
        ApiBaseUrl = url;
        DeviceName = name;

        if (Context.InShell)
        {
            if (changed)
            {
                await _navigator.LogoutAsync("Terminal settings saved. Please sign in again.");
            }

            return;
        }

        await _navigator.ShowLoginAsync(Context.IsFirstRun ? "Terminal configured. Sign in to continue." : "Settings saved.");
    }

    [RelayCommand]
    private Task CancelAsync() => _navigator.ShowLoginAsync();
}
