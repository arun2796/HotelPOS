using CommunityToolkit.Mvvm.ComponentModel;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Shell;

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
