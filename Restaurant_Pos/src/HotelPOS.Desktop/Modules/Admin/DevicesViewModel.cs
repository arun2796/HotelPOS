using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Devices;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Realtime;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Modules.Admin;

public sealed partial class DevicesViewModel : ObservableObject, INavigationAware, IRefreshable
{
    private readonly IDevicesApi _devicesApi;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;
    private readonly IRealtimeClient _realtime;
    private readonly IUiDispatcher _ui;

    public DevicesViewModel(
        IDevicesApi devicesApi,
        IDialogService dialogs,
        INotificationService notifications,
        IRealtimeClient realtime,
        IUiDispatcher ui)
    {
        _devicesApi = devicesApi;
        _dialogs = dialogs;
        _notifications = notifications;
        _realtime = realtime;
        _ui = ui;
    }

    public ObservableCollection<DeviceDto> Devices { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(ToggleActiveText))]
    [NotifyCanExecuteChangedFor(nameof(RenameCommand), nameof(ToggleActiveCommand))]
    private DeviceDto? _selectedDevice;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    public bool HasSelection => SelectedDevice is not null;

    public string ToggleActiveText => SelectedDevice?.IsActive == true ? "Deactivate" : "Activate";

    private IDisposable? _deviceStatusSubscription;

    public async Task OnNavigatedToAsync(object? parameter)
    {
        _deviceStatusSubscription = _realtime.Subscribe<DeviceStatusChangedEvent>(HubEvents.DeviceStatusChanged, OnDeviceStatusChanged);
        await LoadAsync();
    }

    public void OnNavigatedFrom()
    {
        _deviceStatusSubscription?.Dispose();
        _deviceStatusSubscription = null;
    }

    [RelayCommand]
    public Task RefreshAsync() => LoadAsync();

    private void OnDeviceStatusChanged(DeviceStatusChangedEvent evt)
    {
        _ui.Post(() =>
        {
            var match = Devices.FirstOrDefault(d => d.Id == evt.DeviceId);
            if (match is not null)
            {
                var idx = Devices.IndexOf(match);
                Devices[idx] = match with
                {
                    IsOnline = evt.IsOnline,
                    IsActive = evt.IsActive,
                    LastSeenAtUtc = evt.TimestampUtc,
                };

                if (SelectedDevice?.Id == evt.DeviceId)
                {
                    SelectedDevice = Devices[idx];
                }
            }
        });
    }

    private async Task LoadAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _devicesApi.ListAsync();
            if (!result.Success || result.Data is null)
            {
                ErrorMessage = result.Message;
                return;
            }

            var previousId = SelectedDevice?.Id;
            Devices.Clear();
            foreach (var d in result.Data)
            {
                Devices.Add(d);
            }

            SelectedDevice = Devices.FirstOrDefault(d => d.Id == previousId) ?? Devices.FirstOrDefault();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task RenameAsync()
    {
        if (SelectedDevice is null) return;

        var newName = await _dialogs.PromptAsync("Rename terminal", $"Enter a new name for '{SelectedDevice.Name}':", SelectedDevice.Name);
        if (string.IsNullOrWhiteSpace(newName) || newName.Trim() == SelectedDevice.Name) return;

        IsBusy = true;
        try
        {
            var request = new UpdateDeviceRequest
            {
                Name = newName.Trim(),
                StationId = SelectedDevice.StationId,
            };

            var result = await _devicesApi.UpdateAsync(SelectedDevice.Id, request);
            if (result.Success && result.Data is not null)
            {
                _notifications.Success($"Terminal renamed to '{result.Data.Name}'.");
                await LoadAsync();
            }
            else
            {
                _notifications.Error(result.Message, result.CorrelationId);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task ToggleActiveAsync()
    {
        if (SelectedDevice is null) return;

        var willDeactivate = SelectedDevice.IsActive;
        if (willDeactivate)
        {
            if (!await _dialogs.ConfirmAsync(
                "Deactivate terminal",
                $"Are you sure you want to deactivate '{SelectedDevice.Name}'?\n\nThe device connection will be immediately disconnected, and requests from this device will be blocked.",
                "Deactivate",
                destructive: true))
            {
                return;
            }
        }

        IsBusy = true;
        try
        {
            var result = willDeactivate
                ? await _devicesApi.DeactivateAsync(SelectedDevice.Id)
                : await _devicesApi.ActivateAsync(SelectedDevice.Id);

            if (result.Success && result.Data is not null)
            {
                _notifications.Success(willDeactivate ? $"Terminal '{SelectedDevice.Name}' deactivated." : $"Terminal '{SelectedDevice.Name}' activated.");
                await LoadAsync();
            }
            else
            {
                _notifications.Error(result.Message, result.CorrelationId);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }
}
