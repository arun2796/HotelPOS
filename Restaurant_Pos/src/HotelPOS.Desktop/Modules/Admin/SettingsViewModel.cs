using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Desktop.Modules.Common;
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

// One row of the "Notifications" section: a kind of notification with its two per-terminal switches.
public sealed partial class NotificationPreferenceItemViewModel : ObservableObject
{
    private readonly NotificationPreferences _preferences;

    public NotificationPreferenceItemViewModel(NotificationKind kind, NotificationPreferences preferences)
    {
        _preferences = preferences;
        Kind = kind;
        var current = preferences.Get(kind);
        _toast = current.Toast;
        _sound = current.Sound;
    }

    public NotificationKind Kind { get; }

    public string Label => NotificationPreferenceDefaults.Label(Kind);

    [ObservableProperty]
    private bool _toast;

    [ObservableProperty]
    private bool _sound;

    partial void OnToastChanged(bool value) => _preferences.Set(Kind, value, Sound);

    partial void OnSoundChanged(bool value) => _preferences.Set(Kind, Toast, value);
}

public sealed partial class SettingsViewModel : ViewModelBase, INavigationAware, IRefreshable
{
    private readonly IAdminSettingsApi _api;
    private readonly INotificationService _notifications;

    public SettingsViewModel(IAdminSettingsApi api, INotificationService notifications, NotificationPreferences preferences)
    {
        _api = api;
        _notifications = notifications;
        foreach (var kind in NotificationPreferenceDefaults.AllKinds)
        {
            Notifications.Add(new NotificationPreferenceItemViewModel(kind, preferences));
        }
    }

    public ObservableCollection<SettingItemViewModel> Items { get; } = new();

    // The same items under their headings (SettingSections), which the side navigation jumps to.
    public ObservableCollection<SettingGroupViewModel> Groups { get; } = new();

    // Headings for the side navigation: the groups, then the per-terminal Notifications section.
    public ObservableCollection<string> Sections { get; } = new();

    // The heading the side navigation has selected; the view scrolls to it. A deep link (Phase 21 A7) asks for one
    // before the list is loaded, so the request is applied once the sections exist.
    [ObservableProperty]
    private string? _selectedSection;

    public string? RequestedSection { get; set; }

    // Per-terminal toast and sound choices (Phase 20.2); saved as soon as a box is ticked, not by "Save changes".
    public ObservableCollection<NotificationPreferenceItemViewModel> Notifications { get; } = new();

    public Task OnNavigatedToAsync(object? parameter) => LoadAsync();

    public void OnNavigatedFrom()
    {
    }

    public Task RefreshAsync() => Items.Any(i => i.IsDirty) ? Task.CompletedTask : LoadAsync();

    [RelayCommand]
    private Task LoadAsync() => ExecuteAsync(async () =>
    {
        var settings = await FetchAsync(_api.GetAllAsync());
        if (settings is null)
        {
            return;
        }

        Items.Clear();
        // MenuVersion is maintained by the system; it is not an admin setting.
        foreach (var dto in settings.Where(s => s.Key != SettingKeys.MenuVersion))
        {
            Items.Add(new SettingItemViewModel(dto));
        }

        Groups.Clear();
        Sections.Clear();
        foreach (var group in SettingSections.Group(Items))
        {
            Groups.Add(group);
            Sections.Add(group.Name);
        }

        Sections.Add(SettingSections.Notifications);
        if (RequestedSection is { } requested)
        {
            RequestedSection = null;
            SelectedSection = Sections.FirstOrDefault(s => s == requested);
        }
    });

    protected override void OnBusyChanged() => SaveCommand.NotifyCanExecuteChanged();

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

        await ExecuteAsync(async () =>
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
        });
    }
}
