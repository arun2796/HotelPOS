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

public sealed record StationOption(int? Id, string Name);

public sealed partial class DevicesViewModel : ObservableObject, INavigationAware, IRefreshable
{
    private static readonly StationOption NoStation = new(null, "(no station: all tickets)");

    private readonly IDevicesApi _devicesApi;
    private readonly IMenuApi _menuApi;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;
    private readonly IRealtimeClient _realtime;
    private readonly IUiDispatcher _ui;

    public DevicesViewModel(
        IDevicesApi devicesApi,
        IMenuApi menuApi,
        IDialogService dialogs,
        INotificationService notifications,
        IRealtimeClient realtime,
        IUiDispatcher ui)
    {
        _devicesApi = devicesApi;
        _menuApi = menuApi;
        _dialogs = dialogs;
        _notifications = notifications;
        _realtime = realtime;
        _ui = ui;
    }

    public ObservableCollection<DeviceDto> Devices { get; } = new();

    public ObservableCollection<StationOption> Stations { get; } = new() { NoStation };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(ToggleActiveText))]
    [NotifyCanExecuteChangedFor(nameof(RenameCommand), nameof(ToggleActiveCommand), nameof(AssignStationCommand))]
    private DeviceDto? _selectedDevice;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AssignStationCommand))]
    private StationOption? _selectedStation;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    public bool HasSelection => SelectedDevice is not null;

    public string ToggleActiveText => SelectedDevice?.IsActive == true ? "Deactivate" : "Activate";

    private bool CanAssignStation => SelectedDevice is not null && SelectedStation is not null && SelectedStation.Id != SelectedDevice.StationId;

    private IDisposable? _deviceStatusSubscription;

    public async Task OnNavigatedToAsync(object? parameter)
    {
        _deviceStatusSubscription = _realtime.Subscribe<DeviceStatusChangedEvent>(HubEvents.DeviceStatusChanged, OnDeviceStatusChanged);
        await LoadStationsAsync();
        await LoadAsync();
    }

    public void OnNavigatedFrom()
    {
        _deviceStatusSubscription?.Dispose();
        _deviceStatusSubscription = null;
    }

    [RelayCommand]
    public Task RefreshAsync() => LoadAsync();

    partial void OnSelectedDeviceChanged(DeviceDto? value) =>
        SelectedStation = value is null ? null : Stations.FirstOrDefault(s => s.Id == value.StationId) ?? NoStation;

    private void OnDeviceStatusChanged(DeviceStatusChangedEvent evt)
    {
        _ui.Post(() =>
        {
            var match = Devices.FirstOrDefault(d => d.Id == evt.DeviceId);
            if (match is null)
            {
                return;
            }

            // The same event reports renames and (de)activations; only an online/offline edge is a "last seen".
            var index = Devices.IndexOf(match);
            Devices[index] = match with
            {
                Name = string.IsNullOrWhiteSpace(evt.DeviceName) ? match.Name : evt.DeviceName,
                IsOnline = evt.IsOnline,
                IsActive = evt.IsActive,
                LastSeenAtUtc = evt.IsOnline != match.IsOnline ? evt.TimestampUtc : match.LastSeenAtUtc,
            };

            if (SelectedDevice?.Id == evt.DeviceId)
            {
                SelectedDevice = Devices[index];
            }
        });
    }

    private async Task LoadStationsAsync()
    {
        var result = await _menuApi.GetStationsAsync(includeInactive: false);
        if (!result.Success || result.Data is null)
        {
            return;
        }

        Stations.Clear();
        Stations.Add(NoStation);
        foreach (var station in result.Data.OrderBy(s => s.SortOrder))
        {
            Stations.Add(new StationOption(station.Id, station.Name));
        }
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

        await UpdateAsync(newName.Trim(), SelectedDevice.StationId, $"Terminal renamed to '{newName.Trim().ToUpperInvariant()}'.");
    }

    // A kitchen display bound to a station shows only that station's tickets.
    [RelayCommand(CanExecute = nameof(CanAssignStation))]
    private Task AssignStationAsync() =>
        SelectedDevice is null || SelectedStation is null
            ? Task.CompletedTask
            : UpdateAsync(SelectedDevice.Name, SelectedStation.Id, $"'{SelectedDevice.Name}' now shows {SelectedStation.Name}.");

    private async Task UpdateAsync(string name, int? stationId, string successMessage)
    {
        IsBusy = true;
        try
        {
            // The update and (de)activate endpoints answer without data: success alone decides.
            var result = await _devicesApi.UpdateAsync(SelectedDevice!.Id, new UpdateDeviceRequest { Name = name, StationId = stationId });
            if (result.Success)
            {
                _notifications.Success(successMessage);
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
            var name = SelectedDevice.Name;
            var result = willDeactivate
                ? await _devicesApi.DeactivateAsync(SelectedDevice.Id)
                : await _devicesApi.ActivateAsync(SelectedDevice.Id);

            if (result.Success)
            {
                _notifications.Success(willDeactivate ? $"Terminal '{name}' deactivated." : $"Terminal '{name}' activated.");
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
