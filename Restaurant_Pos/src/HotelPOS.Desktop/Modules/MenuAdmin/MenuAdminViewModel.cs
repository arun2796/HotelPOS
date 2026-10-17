using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using HotelPOS.Contracts.Security;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Auth;
using HotelPOS.Desktop.Services.Menu;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Modules.MenuAdmin;

public interface IMenuAdminPage
{
    string Title { get; }

    Task LoadAsync();
}

public sealed partial class MenuAdminViewModel : ObservableObject, INavigationAware, IRefreshable, IDisposable
{
    // Tab titles, also the navigation parameter for a deep link (Phase 21 A7): NavigateToAsync(ModuleRegistry.Menu, MenuAdminViewModel.CategoriesTab).
    public const string CategoriesTab = "Categories";
    public const string ItemsTab = "Items";
    public const string StationsTab = "Stations";

    public MenuAdminViewModel(
        IMenuApi menuApi,
        IMenuCache menuCache,
        ISystemApi systemApi,
        IAuthSession session,
        IDialogService dialogs,
        INotificationService notifications,
        IFilePicker filePicker)
    {
        Items = new MenuItemsPageViewModel(menuApi, menuCache, dialogs, notifications, filePicker);
        Preview = new MenuPreviewViewModel(menuCache, systemApi);
        // Categories then Menu items first (the prompt's Menu Management order); the item editor still opens by default.
        Tabs.Add(new CategoriesPageViewModel(menuApi, dialogs, notifications));
        Tabs.Add(Items);
        Tabs.Add(new ModifiersPageViewModel(menuApi, notifications));
        if (session.User?.Roles.Contains(Roles.Admin) == true)
        {
            Tabs.Add(new TaxesPageViewModel(menuApi, notifications));
            Tabs.Add(new StationsPageViewModel(menuApi, notifications));
        }

        Tabs.Add(Preview);
        _selectedTab = Items;
    }

    public ObservableCollection<IMenuAdminPage> Tabs { get; } = new();

    public MenuItemsPageViewModel Items { get; }

    public MenuPreviewViewModel Preview { get; }

    [ObservableProperty]
    private IMenuAdminPage _selectedTab;

    /// <summary>A tab title as the parameter opens that tab (deep link); anything else keeps the current one.</summary>
    public Task OnNavigatedToAsync(object? parameter)
    {
        if (parameter is string title && Tabs.FirstOrDefault(t => t.Title == title) is { } tab && !ReferenceEquals(tab, SelectedTab))
        {
            // Selecting a tab normally loads it; here RefreshAsync loads it once, so the change handler stays quiet.
            _suppressTabLoad = true;
            try
            {
                SelectedTab = tab;
            }
            finally
            {
                _suppressTabLoad = false;
            }
        }

        return RefreshAsync();
    }

    private bool _suppressTabLoad;

    public void OnNavigatedFrom() => Dispose();

    public void Dispose() => Preview.Dispose();

    public Task RefreshAsync() => SelectedTab.LoadAsync();

    // Each tab reloads when shown, so a category or modifier added on one tab appears in the item editor.
    partial void OnSelectedTabChanged(IMenuAdminPage value)
    {
        if (!_suppressTabLoad)
        {
            _ = value.LoadAsync();
        }
    }
}

internal static class FormNumbers
{
    public static bool TryParseMoney(string text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out value)
        || decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);

    public static string Money(decimal value) => value.ToString("0.##", CultureInfo.CurrentCulture);

    public static string Integer(int value) => value.ToString(CultureInfo.CurrentCulture);
}
