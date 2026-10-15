using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Menu;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Menu;

namespace HotelPOS.Desktop.Modules.MenuAdmin;

public sealed partial class PreviewItemViewModel : ObservableObject
{
    public PreviewItemViewModel(MenuEntryDto item, string priceText)
    {
        Item = item;
        PriceText = priceText;
    }

    public MenuEntryDto Item { get; }

    public string Name => Item.Name;

    public string PriceText { get; }

    public bool IsSoldOut => !Item.IsAvailable;

    public bool HasModifiers => Item.ModifierGroupIds.Count > 0;

    [ObservableProperty]
    private string? _imageFile;
}

public sealed partial class MenuPreviewViewModel : ObservableObject, IMenuAdminPage, IDisposable
{
    private readonly IMenuCache _menuCache;
    private readonly ISystemApi _systemApi;
    private string _currency = string.Empty;
    private bool _listening;

    public MenuPreviewViewModel(IMenuCache menuCache, ISystemApi systemApi)
    {
        _menuCache = menuCache;
        _systemApi = systemApi;
    }

    public string Title => "Preview";

    public ObservableCollection<MenuCategoryDto> Categories { get; } = new();

    public ObservableCollection<PreviewItemViewModel> Items { get; } = new();

    [ObservableProperty]
    private MenuCategoryDto? _selectedCategory;

    [ObservableProperty]
    private string _versionText = string.Empty;

    public async Task LoadAsync()
    {
        if (!_listening)
        {
            _menuCache.Changed += OnMenuChanged;
            _listening = true;
        }

        if (string.IsNullOrEmpty(_currency))
        {
            var settings = await _systemApi.GetPublicSettingsAsync();
            _currency = settings.Data?.FirstOrDefault(s => s.Key == SettingKeys.CurrencySymbol)?.Value ?? string.Empty;
        }

        await _menuCache.RefreshAsync();
        Rebuild();
    }

    public void Dispose()
    {
        if (_listening)
        {
            _menuCache.Changed -= OnMenuChanged;
            _listening = false;
        }
    }

    partial void OnSelectedCategoryChanged(MenuCategoryDto? value) => ShowItems();

    private void OnMenuChanged(object? sender, EventArgs e) => Rebuild();

    private void Rebuild()
    {
        var menu = _menuCache.Menu;
        VersionText = menu is null ? "Menu not loaded" : $"Menu version {menu.Version} · {menu.Items.Count} items";
        var selectedId = SelectedCategory?.Id;
        Categories.Clear();
        foreach (var category in menu?.Categories ?? Array.Empty<MenuCategoryDto>())
        {
            Categories.Add(category);
        }

        SelectedCategory = Categories.FirstOrDefault(c => c.Id == selectedId) ?? Categories.FirstOrDefault();
        ShowItems();
    }

    private void ShowItems()
    {
        Items.Clear();
        var menu = _menuCache.Menu;
        if (menu is null || SelectedCategory is null)
        {
            return;
        }

        foreach (var item in menu.Items.Where(i => i.CategoryId == SelectedCategory.Id))
        {
            var tile = new PreviewItemViewModel(item, $"{_currency}{item.Price.ToString("N2", CultureInfo.CurrentCulture)}");
            Items.Add(tile);
            _ = LoadImageAsync(tile);
        }
    }

    private async Task LoadImageAsync(PreviewItemViewModel tile) => tile.ImageFile = await _menuCache.GetImageFileAsync(tile.Item.ImageUrl);
}
