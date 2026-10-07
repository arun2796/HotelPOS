using CommunityToolkit.Mvvm.ComponentModel;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Shell;

/// <summary>DataContext of the main window: the current top-level screen plus the dialog and toast layers.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    public MainViewModel(DialogService dialogs, NotificationService notifications)
    {
        Dialogs = dialogs;
        Notifications = notifications;
    }

    [ObservableProperty]
    private object? _currentScreen;

    public DialogService Dialogs { get; }

    public NotificationService Notifications { get; }
}
