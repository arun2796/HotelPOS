using CommunityToolkit.Mvvm.ComponentModel;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Modules.Admin;

/// <summary>Admin/Manager "Sections &amp; Tables" page: a Tables tab and a Sections tab.</summary>
public sealed partial class FloorViewModel : ObservableObject, INavigationAware, IRefreshable
{
    public const int TablesTab = 0;
    public const int SectionsTab = 1;

    public FloorViewModel(IFloorApi floorApi, IDialogService dialogs, INotificationService notifications)
    {
        Tables = new TablesViewModel(floorApi, dialogs, notifications);
        Sections = new SectionsViewModel(floorApi, notifications);
    }

    public TablesViewModel Tables { get; }

    public SectionsViewModel Sections { get; }

    public IReadOnlyList<string> Tabs { get; } = new[] { "Tables", "Sections" };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTablesTab), nameof(IsSectionsTab))]
    private int _selectedTab = TablesTab;

    public bool IsTablesTab => SelectedTab == TablesTab;

    public bool IsSectionsTab => SelectedTab == SectionsTab;

    public Task OnNavigatedToAsync(object? parameter) => RefreshAsync();

    public void OnNavigatedFrom()
    {
    }

    public Task RefreshAsync() => IsSectionsTab ? Sections.LoadAsync() : Tables.LoadAsync();

    // Each tab reloads when shown, so sections edited on one tab appear in the other's pickers.
    partial void OnSelectedTabChanged(int value) => _ = RefreshAsync();
}
