using CommunityToolkit.Mvvm.ComponentModel;
using HotelPOS.Desktop.Modules.Expenses;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Auth;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Printing;
using HotelPOS.Desktop.Services.Realtime;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Modules.Admin;

// Restaurant Settings hub: the shared settings and (Phase 14) the print templates, plus (Phase 22) expense categories.
public sealed partial class SettingsHubViewModel : ObservableObject, INavigationAware, IRefreshable
{
    public const int SettingsTab = 0;
    public const int TemplatesTab = 1;
    public const int CategoriesTab = 2;

    public SettingsHubViewModel(
        IAdminSettingsApi settingsApi,
        ITemplatesApi templatesApi,
        IBillingApi billingApi,
        IExpensesApi expensesApi,
        IDocumentPrinter printer,
        IPrinterProfiles profiles,
        IFilePicker files,
        INotificationService notifications,
        IRealtimeClient realtime,
        NotificationPreferences preferences,
        IAuthSession session)
    {
        Settings = new SettingsViewModel(settingsApi, notifications, preferences);
        Templates = new TemplateStudioViewModel(templatesApi, billingApi, printer, profiles, files, notifications, realtime);
        Categories = new ExpenseCategoriesViewModel(expensesApi, notifications, session);
    }

    public SettingsViewModel Settings { get; }

    public TemplateStudioViewModel Templates { get; }

    public ExpenseCategoriesViewModel Categories { get; }

    public IReadOnlyList<string> Tabs { get; } = new[] { "Restaurant settings", "Print templates", "Expense categories" };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSettingsTab), nameof(IsTemplatesTab), nameof(IsCategoriesTab))]
    private int _selectedTab = SettingsTab;

    public bool IsSettingsTab => SelectedTab == SettingsTab;

    public bool IsTemplatesTab => SelectedTab == TemplatesTab;

    public bool IsCategoriesTab => SelectedTab == CategoriesTab;

    /// <summary>
    /// Deep links (Phase 21 A7): an <c>int</c> tab (<see cref="SettingsTab"/> / <see cref="TemplatesTab"/>), or a section name
    /// (<see cref="SettingSections"/>) that opens the settings tab scrolled to that heading.
    /// </summary>
    public Task OnNavigatedToAsync(object? parameter)
    {
        var tab = parameter switch
        {
            int index => index,
            string section => SettingsTab,
            _ => (int?)null,
        };
        if (parameter is string requested)
        {
            Settings.RequestedSection = requested;
        }

        if (tab is { } chosen && chosen != SelectedTab)
        {
            // Selecting a tab normally refreshes it; here RefreshAsync below does that once.
            _suppressRefresh = true;
            try
            {
                SelectedTab = chosen;
            }
            finally
            {
                _suppressRefresh = false;
            }
        }

        return RefreshAsync();
    }

    private bool _suppressRefresh;

    public void OnNavigatedFrom() => Templates.Unload();

    public Task RefreshAsync() => SelectedTab switch
    {
        TemplatesTab => Templates.LoadAsync(),
        CategoriesTab => Categories.LoadAsync(),
        _ => Settings.RefreshAsync(),
    };

    partial void OnSelectedTabChanged(int value)
    {
        if (!_suppressRefresh)
        {
            _ = RefreshAsync();
        }
    }
}
