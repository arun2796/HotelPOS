using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Menu;
using HotelPOS.Desktop.Modules.Common;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Menu;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Modules.MenuAdmin;

public sealed record LookupOption(int? Id, string Name);

public sealed partial class ModifierChoiceViewModel : ObservableObject
{
    public ModifierChoiceViewModel(int id, string name, string summary)
    {
        Id = id;
        Name = name;
        Summary = summary;
    }

    public int Id { get; }

    public string Name { get; }

    public string Summary { get; }

    [ObservableProperty]
    private bool _isSelected;
}

public sealed partial class MenuItemEditorViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(SaveText), nameof(CanEditPicture))]
    private bool _isNew;

    [ObservableProperty]
    private int _itemId;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _code = string.Empty;

    [ObservableProperty]
    private CategoryDto? _category;

    [ObservableProperty]
    private string _price = string.Empty;

    [ObservableProperty]
    private LookupOption? _tax;

    [ObservableProperty]
    private StationDto? _station;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _sortOrder = "0";

    [ObservableProperty]
    private bool _isAvailable = true;

    [ObservableProperty]
    private bool _isActive = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPicture))]
    private string? _imageUrl;

    [ObservableProperty]
    private string? _imageFile;

    [ObservableProperty]
    private string _rowVersion = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    public ObservableCollection<CategoryDto> Categories { get; } = new();

    public ObservableCollection<LookupOption> Taxes { get; } = new();

    public ObservableCollection<StationDto> Stations { get; } = new();

    public ObservableCollection<ModifierChoiceViewModel> ModifierGroups { get; } = new();

    public string Title => IsNew ? "New item" : "Edit item";

    public string SaveText => IsNew ? "Create item" : "Save changes";

    public bool HasPicture => ImageUrl is not null;

    public bool CanEditPicture => !IsNew;

    public void SetLookups(IEnumerable<CategoryDto> categories, IEnumerable<TaxDto> taxes, IEnumerable<StationDto> stations, IEnumerable<ModifierGroupDto> groups)
    {
        Replace(Categories, categories.Where(c => c.IsActive));
        Replace(Taxes, new[] { new LookupOption(null, "No tax") }.Concat(taxes.Where(t => t.IsActive).Select(t => new LookupOption(t.Id, t.Name))));
        Replace(Stations, stations.Where(s => s.IsActive));
        Replace(ModifierGroups, groups.Where(g => g.IsActive).Select(g => new ModifierChoiceViewModel(
            g.Id,
            g.Name,
            $"{(g.MinSelections == 0 ? "Optional" : $"Pick {g.MinSelections}")}, up to {g.MaxSelections} · {string.Join(", ", g.Options.Where(o => o.IsActive).Select(o => o.Name))}")));
    }

    public void BeginCreate(int? categoryId)
    {
        IsNew = true;
        ItemId = 0;
        Name = Code = Description = RowVersion = string.Empty;
        Price = string.Empty;
        SortOrder = "0";
        IsAvailable = IsActive = true;
        ImageUrl = ImageFile = null;
        Category = Categories.FirstOrDefault(c => c.Id == categoryId) ?? Categories.FirstOrDefault();
        Tax = Taxes.Skip(1).FirstOrDefault() ?? Taxes.FirstOrDefault();
        Station = Stations.FirstOrDefault();
        foreach (var group in ModifierGroups)
        {
            group.IsSelected = false;
        }

        ErrorMessage = null;
        IsOpen = true;
    }

    public void BeginEdit(MenuItemDto item)
    {
        IsNew = false;
        ItemId = item.Id;
        Name = item.Name;
        Code = item.Code ?? string.Empty;
        Description = item.Description ?? string.Empty;
        Price = FormNumbers.Money(item.Price);
        SortOrder = FormNumbers.Integer(item.SortOrder);
        IsAvailable = item.IsAvailable;
        IsActive = item.IsActive;
        ImageUrl = item.ImageUrl;
        ImageFile = null;
        RowVersion = item.RowVersion;
        Category = Categories.FirstOrDefault(c => c.Id == item.CategoryId);
        Tax = Taxes.FirstOrDefault(t => t.Id == item.TaxId) ?? Taxes.FirstOrDefault();
        Station = Stations.FirstOrDefault(s => s.Id == item.PreparationStationId);
        foreach (var group in ModifierGroups)
        {
            group.IsSelected = item.ModifierGroupIds.Contains(group.Id);
        }

        ErrorMessage = null;
        IsOpen = true;
    }

    public void Close()
    {
        IsOpen = false;
        ErrorMessage = null;
    }

    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            return "Enter the item name.";
        }

        if (Category is null || Station is null)
        {
            return "Select a category and a preparation station.";
        }

        if (!FormNumbers.TryParseMoney(Price, out var price) || price < 0 || decimal.Round(price, 2) != price)
        {
            return "Enter a price such as 180 or 180.50.";
        }

        return int.TryParse(SortOrder, out var order) && order is >= 0 and <= MenuLimits.MaxSortOrder
            ? null
            : $"Sort order must be a number from 0 to {MenuLimits.MaxSortOrder}.";
    }

    public CreateMenuItemRequest ToCreateRequest()
    {
        FormNumbers.TryParseMoney(Price, out var price);
        return new CreateMenuItemRequest
        {
            CategoryId = Category!.Id,
            Name = Name.Trim(),
            Code = string.IsNullOrWhiteSpace(Code) ? null : Code.Trim(),
            Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
            Price = price,
            TaxId = Tax?.Id,
            PreparationStationId = Station!.Id,
            SortOrder = int.Parse(SortOrder),
            IsAvailable = IsAvailable,
            ModifierGroupIds = SelectedGroups(),
        };
    }

    public UpdateMenuItemRequest ToUpdateRequest()
    {
        var create = ToCreateRequest();
        return new UpdateMenuItemRequest
        {
            CategoryId = create.CategoryId,
            Name = create.Name,
            Code = create.Code,
            Description = create.Description,
            Price = create.Price,
            TaxId = create.TaxId,
            PreparationStationId = create.PreparationStationId,
            SortOrder = create.SortOrder,
            IsAvailable = create.IsAvailable,
            ModifierGroupIds = create.ModifierGroupIds,
            IsActive = IsActive,
            RowVersion = RowVersion,
        };
    }

    private List<int> SelectedGroups() => ModifierGroups.Where(g => g.IsSelected).Select(g => g.Id).ToList();

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }
}

public sealed partial class MenuItemsPageViewModel : ObservableObject, IMenuAdminPage
{
    private readonly IMenuApi _menuApi;
    private readonly IMenuCache _menuCache;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;
    private readonly IFilePicker _filePicker;

    public MenuItemsPageViewModel(IMenuApi menuApi, IMenuCache menuCache, IDialogService dialogs, INotificationService notifications, IFilePicker filePicker)
    {
        _menuApi = menuApi;
        _menuCache = menuCache;
        _dialogs = dialogs;
        _notifications = notifications;
        _filePicker = filePicker;
    }

    public string Title => "Items";

    public ObservableCollection<MenuItemDto> Items { get; } = new();

    public ObservableCollection<CategoryDto> CategoryFilters { get; } = new();

    public MenuItemEditorViewModel Editor { get; } = new();

    [ObservableProperty]
    private CategoryDto? _categoryFilter;

    [ObservableProperty]
    private string _search = string.Empty;

    [ObservableProperty]
    private bool _showInactive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(SoldOutText))]
    [NotifyCanExecuteChangedFor(nameof(EditCommand), nameof(ToggleSoldOutCommand))]
    private MenuItemDto? _selectedItem;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    public bool HasSelection => SelectedItem is not null;

    public string SoldOutText => SelectedItem?.IsAvailable == false ? "Mark available" : "Mark sold out";

    public async Task LoadAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var categories = await _menuApi.GetCategoriesAsync(includeInactive: true);
            var taxes = await _menuApi.GetTaxesAsync(includeInactive: false);
            var stations = await _menuApi.GetStationsAsync(includeInactive: false);
            var groups = await _menuApi.GetModifierGroupsAsync(includeInactive: false);
            var failure = new[] { categories.Success ? null : categories.Message, taxes.Success ? null : taxes.Message, stations.Success ? null : stations.Message, groups.Success ? null : groups.Message }
                .FirstOrDefault(m => m is not null);
            if (failure is not null)
            {
                ErrorMessage = failure;
                return;
            }

            Editor.SetLookups(categories.Data!, taxes.Data!, stations.Data!, groups.Data!);
            var filterId = CategoryFilter?.Id ?? 0;
            CategoryFilters.Clear();
            CategoryFilters.Add(new CategoryDto { Id = 0, Name = "All categories", IsActive = true });
            foreach (var category in categories.Data!)
            {
                CategoryFilters.Add(category);
            }

            CategoryFilter = CategoryFilters.FirstOrDefault(c => c.Id == filterId) ?? CategoryFilters[0];
            await LoadItemsAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnCategoryFilterChanged(CategoryDto? value) => _ = LoadItemsAsync();

    partial void OnShowInactiveChanged(bool value) => _ = LoadItemsAsync();

    partial void OnSelectedItemChanged(MenuItemDto? value)
    {
        if (Editor.IsOpen && value is not null && !Editor.IsNew && value.Id != Editor.ItemId)
        {
            Editor.Close();
        }
    }

    [RelayCommand]
    private Task SearchItemsAsync() => LoadItemsAsync();

    [RelayCommand]
    private Task RefreshListAsync() => LoadAsync();

    [RelayCommand]
    private void NewItem()
    {
        if (Editor.Categories.Count == 0 || Editor.Stations.Count == 0)
        {
            _notifications.Warning("Create an active category and preparation station first.");
            return;
        }

        Editor.BeginCreate(CategoryFilter?.Id);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task EditAsync()
    {
        if (SelectedItem is not null)
        {
            Editor.BeginEdit(SelectedItem);
            await LoadPictureAsync();
        }
    }

    [RelayCommand]
    private void CancelEdit() => Editor.Close();

    [RelayCommand]
    private async Task SaveItemAsync()
    {
        Editor.ErrorMessage = Editor.Validate();
        if (Editor.ErrorMessage is not null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = Editor.IsNew
                ? await _menuApi.CreateItemAsync(Editor.ToCreateRequest())
                : await _menuApi.UpdateItemAsync(Editor.ItemId, Editor.ToUpdateRequest());

            if (result.Success && result.Data is not null)
            {
                _notifications.Success(Editor.IsNew ? $"{result.Data.Name} created. You can now add a picture." : $"{result.Data.Name} updated.");
                await LoadItemsAsync(result.Data.Id);
                Editor.BeginEdit(result.Data);
                await LoadPictureAsync();
                return;
            }

            if (result.HasError(ErrorCodes.ConcurrencyConflict) && result.Data is not null)
            {
                Editor.BeginEdit(result.Data);
                Editor.ErrorMessage = result.Message;
                await LoadItemsAsync(result.Data.Id);
                return;
            }

            Editor.ErrorMessage = ApiFailures.Describe(result);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ChoosePictureAsync()
    {
        if (Editor.IsNew || _filePicker.PickImage() is not { } path)
        {
            return;
        }

        var info = new FileInfo(path);
        if (info.Length > MenuLimits.MaxImageBytes)
        {
            Editor.ErrorMessage = $"The picture is larger than {MenuLimits.MaxImageBytes / (1024 * 1024)} MB. Choose a smaller one.";
            return;
        }

        IsBusy = true;
        try
        {
            await using var stream = File.OpenRead(path);
            var result = await _menuApi.UploadImageAsync(Editor.ItemId, stream, info.Name);
            await ApplyPictureResultAsync(result, "Picture saved.");
        }
        catch (IOException ex)
        {
            Editor.ErrorMessage = $"Could not read the file: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RemovePictureAsync()
    {
        if (Editor.IsNew || !Editor.HasPicture
            || !await _dialogs.ConfirmAsync("Remove picture", $"Remove the picture of {Editor.Name}?", "Remove", destructive: true))
        {
            return;
        }

        await ApplyPictureResultAsync(await _menuApi.RemoveImageAsync(Editor.ItemId), "Picture removed.");
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task ToggleSoldOutAsync()
    {
        var item = SelectedItem;
        if (item is null)
        {
            return;
        }

        var result = await _menuApi.SetAvailabilityAsync(item.Id, !item.IsAvailable);
        if (result.Success && result.Data is not null)
        {
            _notifications.Success(result.Data.IsAvailable ? $"{item.Name} is available again." : $"{item.Name} is sold out.");
        }
        else
        {
            _notifications.Error(ApiFailures.Describe(result), result.CorrelationId);
        }

        await LoadItemsAsync(item.Id);
    }

    private async Task ApplyPictureResultAsync(ApiResult<MenuItemDto> result, string successMessage)
    {
        if (result.Success && result.Data is not null)
        {
            // The picture has its own endpoint, so keep the user's unsaved edits; only the row version moves on.
            Editor.ImageUrl = result.Data.ImageUrl;
            Editor.RowVersion = result.Data.RowVersion;
            Editor.ErrorMessage = null;
            _notifications.Success(successMessage);
            await LoadPictureAsync();
            await LoadItemsAsync(result.Data.Id);
        }
        else
        {
            Editor.ErrorMessage = ApiFailures.Describe(result);
        }
    }

    private async Task LoadPictureAsync() => Editor.ImageFile = await _menuCache.GetImageFileAsync(Editor.ImageUrl);

    private async Task LoadItemsAsync(int? selectId = null)
    {
        var keep = selectId ?? SelectedItem?.Id;
        var result = await _menuApi.GetItemsAsync(new MenuItemQuery
        {
            CategoryId = CategoryFilter is { Id: > 0 } filter ? filter.Id : null,
            Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
            IncludeInactive = ShowInactive,
        });
        if (!result.Success || result.Data is null)
        {
            ErrorMessage = result.Message;
            return;
        }

        Items.Clear();
        foreach (var item in result.Data)
        {
            Items.Add(item);
        }

        SelectedItem = Items.FirstOrDefault(i => i.Id == keep);
    }
}
