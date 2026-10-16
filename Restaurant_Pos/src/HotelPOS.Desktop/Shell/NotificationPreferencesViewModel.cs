using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Desktop.Modules.Admin;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Shell;

/// <summary>
/// The "Notifications…" dialog opened from the shell's user area, available to every role. It lists the same per-kind
/// Pop-up / Sound switches as the Admin Settings screen and writes straight to the same <see cref="NotificationPreferences"/> store.
/// </summary>
public sealed partial class NotificationPreferencesViewModel : ObservableObject
{
    private readonly Action _close;

    public NotificationPreferencesViewModel(NotificationPreferences preferences, Action close)
    {
        _close = close;
        foreach (var kind in NotificationPreferenceDefaults.AllKinds)
        {
            Notifications.Add(new NotificationPreferenceItemViewModel(kind, preferences));
        }
    }

    public ObservableCollection<NotificationPreferenceItemViewModel> Notifications { get; } = new();

    [RelayCommand]
    private void Close() => _close();
}
