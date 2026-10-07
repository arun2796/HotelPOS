using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Enums;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Modules.Admin;

public sealed partial class SettingItemViewModel : ObservableObject
{
    public SettingItemViewModel(SettingDto dto)
    {
        Key = dto.Key;
        Description = dto.Description;
        DataType = dto.DataType;
        IsPublic = dto.IsPublic;
        OriginalValue = dto.Value;
        Value = dto.Value;
    }

    public string Key { get; }

    /// <summary>"KitchenWarnMinutes" -> "Kitchen warn minutes".</summary>
    public string DisplayName => Humanize(Key);

    public string? Description { get; }

    public SettingDataType DataType { get; }

    public bool IsPublic { get; }

    public bool IsBoolean => DataType == SettingDataType.Bool;

    public bool IsText => !IsBoolean;

    public string Hint => DataType switch
    {
        SettingDataType.Int => "Whole number",
        SettingDataType.Decimal => "Number",
        SettingDataType.Time => "Time, HH:mm",
        SettingDataType.Json => "JSON",
        _ => string.Empty,
    };

    public string OriginalValue { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty), nameof(BoolValue))]
    private string _value = string.Empty;

    [ObservableProperty]
    private string? _error;

    public bool IsDirty => Value != OriginalValue;

    public bool BoolValue
    {
        get => string.Equals(Value, "true", StringComparison.OrdinalIgnoreCase);
        set => Value = value ? "true" : "false";
    }

    public void AcceptSaved(string value)
    {
        OriginalValue = value;
        Value = value;
        Error = null;
        OnPropertyChanged(nameof(IsDirty));
    }

    private static string Humanize(string key)
    {
        var words = Regex.Replace(key, "(?<=[a-z0-9])([A-Z])", " $1").ToLowerInvariant();
        return char.ToUpperInvariant(words[0]) + words[1..];
    }
}

/// <summary>Admin: restaurant settings stored on the server (name, invoice details, thresholds...).</summary>
public sealed partial class SettingsViewModel : ObservableObject, INavigationAware, IRefreshable
{
    private readonly IAdminSettingsApi _api;
    private readonly INotificationService _notifications;

    public SettingsViewModel(IAdminSettingsApi api, INotificationService notifications)
    {
        _api = api;
        _notifications = notifications;
    }

    public ObservableCollection<SettingItemViewModel> Items { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    public Task OnNavigatedToAsync(object? parameter) => LoadAsync();

    public void OnNavigatedFrom()
    {
    }

    public Task RefreshAsync() => Items.Any(i => i.IsDirty) ? Task.CompletedTask : LoadAsync();

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _api.GetAllAsync();
            if (!result.Success || result.Data is null)
            {
                ErrorMessage = result.Message;
                return;
            }

            Items.Clear();
            // MenuVersion is maintained by the system; it is not an admin setting.
            foreach (var dto in result.Data.Where(s => s.Key != SettingKeys.MenuVersion))
            {
                Items.Add(new SettingItemViewModel(dto));
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanSave() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        var dirty = Items.Where(i => i.IsDirty).ToList();
        if (dirty.Count == 0)
        {
            _notifications.Info("Nothing to save.");
            return;
        }

        foreach (var item in Items)
        {
            item.Error = null;
        }

        IsBusy = true;
        try
        {
            var result = await _api.UpdateAsync(new UpdateSettingsRequest
            {
                Items = dirty.Select(i => new SettingValue { Key = i.Key, Value = i.Value }).ToList(),
            });

            if (result.Success && result.Data is not null)
            {
                foreach (var saved in result.Data)
                {
                    Items.FirstOrDefault(i => i.Key == saved.Key)?.AcceptSaved(saved.Value);
                }

                _notifications.Success($"{dirty.Count} setting(s) saved.");
                return;
            }

            if (result.IsConnectionFailure)
            {
                _notifications.Error("Connection unavailable. The settings were NOT saved.");
                return;
            }

            foreach (var error in result.Errors.Where(e => e.Field is not null))
            {
                var item = Items.FirstOrDefault(i => string.Equals(i.Key, error.Field, StringComparison.OrdinalIgnoreCase));
                if (item is not null)
                {
                    item.Error = error.Message;
                }
            }

            ErrorMessage = result.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
