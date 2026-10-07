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

/// <summary>A tab of the Menu page.</summary>
public interface IMenuAdminPage
{
    string Title { get; }

    Task LoadAsync();
}

/// <summary>
/// Admin/Manager "Menu" page: items, categories, modifiers, taxes and stations (the last two editable by Admin
/// only, so only Admin sees them), and a live preview of the ordering menu.
/// </summary>
public sealed partial class MenuAdminViewModel : ObservableObject, INavigationAware, IRefreshable, IDisposable
{
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
        Tabs.Add(Items);
        Tabs.Add(new CategoriesPageViewModel(menuApi, dialogs, notifications));
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

    public Task OnNavigatedToAsync(object? parameter) => RefreshAsync();

    public void OnNavigatedFrom() => Dispose();

    public void Dispose() => Preview.Dispose();

    public Task RefreshAsync() => SelectedTab.LoadAsync();

    // Each tab reloads when shown, so a category or modifier added on one tab appears in the item editor.
    partial void OnSelectedTabChanged(IMenuAdminPage value) => _ = value.LoadAsync();
}

/// <summary>Number parsing for the editor forms: the terminal's culture first, then "1234.50".</summary>
internal static class FormNumbers
{
    public static bool TryParseMoney(string text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out value)
        || decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);

    public static string Money(decimal value) => value.ToString("0.##", CultureInfo.CurrentCulture);

    public static string Integer(int value) => value.ToString(CultureInfo.CurrentCulture);
}
