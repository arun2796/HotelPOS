using CommunityToolkit.Mvvm.ComponentModel;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Printing;
using HotelPOS.Desktop.Services.Realtime;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Modules.Admin;

// Restaurant Settings hub: the shared settings and (Phase 14) the print templates.
public sealed partial class SettingsHubViewModel : ObservableObject, INavigationAware, IRefreshable
{
    public const int SettingsTab = 0;
    public const int TemplatesTab = 1;

    public SettingsHubViewModel(
        IAdminSettingsApi settingsApi,
        ITemplatesApi templatesApi,
        IBillingApi billingApi,
        IDocumentPrinter printer,
        IPrinterProfiles profiles,
        IFilePicker files,
        INotificationService notifications,
        IRealtimeClient realtime)
    {
        Settings = new SettingsViewModel(settingsApi, notifications);
        Templates = new TemplateStudioViewModel(templatesApi, billingApi, printer, profiles, files, notifications, realtime);
    }

    public SettingsViewModel Settings { get; }

    public TemplateStudioViewModel Templates { get; }

    public IReadOnlyList<string> Tabs { get; } = new[] { "Restaurant settings", "Print templates" };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSettingsTab), nameof(IsTemplatesTab))]
    private int _selectedTab = SettingsTab;

    public bool IsSettingsTab => SelectedTab == SettingsTab;

    public bool IsTemplatesTab => SelectedTab == TemplatesTab;

    public Task OnNavigatedToAsync(object? parameter)
    {
        if (parameter is int tab)
        {
            SelectedTab = tab;
        }

        return RefreshAsync();
    }

    public void OnNavigatedFrom() => Templates.Unload();

    public Task RefreshAsync() => IsTemplatesTab ? Templates.LoadAsync() : Settings.RefreshAsync();

    partial void OnSelectedTabChanged(int value) => _ = RefreshAsync();
}
