using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Menu;
using HotelPOS.Desktop.Modules.Common;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Menu;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Modules.MenuAdmin;

public sealed record LookupOption(int? Id, string Name);

// The STOCK column of the items grid: "42 left", "Approx. 80 left", or empty when the item has no count.
public sealed class MenuItemStockTextConverter : IValueConverter
{
    public static string Describe(StockTrackingMode mode, int? remaining)
    {
        if (mode == StockTrackingMode.None || remaining is not { } left)
        {
            return string.Empty;
        }

        return mode == StockTrackingMode.Approximate
            ? string.Create(CultureInfo.CurrentCulture, $"Approx. {left} left")
            : string.Create(CultureInfo.CurrentCulture, $"{left} left");
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is MenuItemDto item ? Describe(item.StockMode, item.StockRemaining) : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

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

    // Parcel/packaging charge added per unit on take-away and delivery orders; 0 means none.
    [ObservableProperty]
    private string _packagingCharge = "0";

    [ObservableProperty]
    private LookupOption? _tax;

    [ObservableProperty]
    private StationDto? _station;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _hsnSac = string.Empty;

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

    // A picture chosen while the item is still new: shown at once, uploaded right after the item is created.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPicture))]
    private string? _pendingImagePath;

    [ObservableProperty]
    private string _rowVersion = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    public ObservableCollection<CategoryDto> Categories { get; } = new();

    public ObservableCollection<LookupOption> Taxes { get; } = new();

    public ObservableCollection<StationDto> Stations { get; } = new();

    public ObservableCollection<ModifierChoiceViewModel> ModifierGroups { get; } = new();

    // The Portions & prices table: Quarter, Half, Full... Exactly one default; at least one on.
    public ObservableCollection<PortionRowViewModel> Portions { get; } = new();

    public string Title => IsNew ? "New item" : "Edit item";

    public string SaveText => IsNew ? "Create item" : "Save changes";

    public bool HasPicture => ImageUrl is not null || PendingImagePath is not null;

    public bool CanEditPicture => true;

    public bool CanAddPortion => Portions.Count < MenuLimits.MaxPortions;

    [RelayCommand]
    private void AddPortion()
    {
        if (Portions.Count >= MenuLimits.MaxPortions)
        {
            return;
        }

        Portions.Add(new PortionRowViewModel());
        OnPropertyChanged(nameof(CanAddPortion));
    }

    // The last portion cannot be removed; removing the default hands the default to the first row left.
    [RelayCommand]
    private void RemovePortion(PortionRowViewModel? row)
    {
        if (row is null || Portions.Count <= 1 || !Portions.Remove(row))
        {
            return;
        }

        if (row.IsDefault)
        {
            SetDefault(Portions[0]);
        }

        OnPropertyChanged(nameof(CanAddPortion));
    }

    // One default at a time; the default is always on.
    [RelayCommand]
    private void SetDefault(PortionRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        foreach (var portion in Portions)
        {
            portion.IsDefault = ReferenceEquals(portion, row);
        }

        row.IsActive = true;
    }

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
        Name = Code = Description = RowVersion = HsnSac = string.Empty;
        PackagingCharge = "0";
        SortOrder = "0";
        IsAvailable = IsActive = true;
        ImageUrl = ImageFile = PendingImagePath = null;
        Portions.Clear();
        Portions.Add(new PortionRowViewModel { Name = "Regular", IsDefault = true });
        OnPropertyChanged(nameof(CanAddPortion));
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
        HsnSac = item.HsnSac ?? string.Empty;
        PackagingCharge = FormNumbers.Money(item.PackagingCharge);
        SortOrder = FormNumbers.Integer(item.SortOrder);
        IsAvailable = item.IsAvailable;
        IsActive = item.IsActive;
        ImageUrl = item.ImageUrl;
        ImageFile = PendingImagePath = null;
        Portions.Clear();
        foreach (var portion in item.Portions.OrderBy(p => p.SortOrder).ThenBy(p => p.Id))
        {
            Portions.Add(new PortionRowViewModel
            {
                Id = portion.Id,
                Name = portion.Name,
                QuantityText = portion.Quantity?.ToString("0.###", CultureInfo.CurrentCulture) ?? string.Empty,
                Unit = portion.Unit ?? string.Empty,
                PriceText = FormNumbers.Money(portion.Price),
                IsDefault = portion.IsDefault,
                IsActive = portion.IsActive,
            });
        }

        if (Portions.Count == 0)
        {
            // A server that does not know portions: the item is its one price.
            Portions.Add(new PortionRowViewModel { Name = "Regular", PriceText = FormNumbers.Money(item.Price), IsDefault = true });
        }

        OnPropertyChanged(nameof(CanAddPortion));
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

    // The portion whose chip was clicked in the list: its row is highlighted in the form.
    public void FocusPortion(int portionId)
    {
        foreach (var row in Portions)
        {
            row.IsFocused = row.Id == portionId;
        }
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
            return "Select a category and a source (kitchen).";
        }

        if (ValidatePortions() is { } portionError)
        {
            return portionError;
        }

        if (!FormNumbers.TryParseMoney(PackagingCharge, out var packaging) || packaging < 0 || decimal.Round(packaging, 2) != packaging)
        {
            return "Enter a packaging charge such as 0 or 10.50.";
        }

        return int.TryParse(SortOrder, out var order) && order is >= 0 and <= MenuLimits.MaxSortOrder
            ? null
            : $"Sort order must be a number from 0 to {MenuLimits.MaxSortOrder}.";
    }

    // Checks the Portions table; the message names the row and the row shows it too. Null when the table is fine.
    private string? ValidatePortions()
    {
        foreach (var row in Portions)
        {
            row.Error = null;
        }

        string? Fail(PortionRowViewModel row, string message)
        {
            row.Error = message;
            return message;
        }

        if (Portions.Count == 0)
        {
            return "Add at least one portion.";
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < Portions.Count; i++)
        {
            var row = Portions[i];
            var name = row.Name.Trim();
            if (name.Length == 0)
            {
                return Fail(row, $"Enter a name for portion {i + 1}.");
            }

            if (name.Length > MenuLimits.PortionNameMaxLength)
            {
                return Fail(row, $"A portion name can have at most {MenuLimits.PortionNameMaxLength} characters.");
            }

            if (!seen.Add(name))
            {
                return Fail(row, $"The portion name {name} is used more than once.");
            }

            if (!FormNumbers.TryParseMoney(row.PriceText, out var price) || price < 0 || price > MenuLimits.MaxPrice || decimal.Round(price, 2) != price)
            {
                return Fail(row, $"Enter a price for {name} such as 180 or 180.50.");
            }

            var hasQuantity = !string.IsNullOrWhiteSpace(row.QuantityText);
            if (hasQuantity && (!FormNumbers.TryParseMoney(row.QuantityText, out var quantity) || quantity <= 0 || quantity > MenuLimits.PortionQuantityMax))
            {
                return Fail(row, $"Enter a quantity above 0 for {name}, or leave it empty.");
            }

            if (!hasQuantity && !string.IsNullOrWhiteSpace(row.Unit))
            {
                return Fail(row, $"Enter a quantity for {name}, or clear its unit.");
            }
        }

        if (Portions.Count(p => p.IsDefault) != 1)
        {
            return Fail(Portions.FirstOrDefault(p => p.IsDefault) ?? Portions[0], "Choose exactly one default portion.");
        }

        if (!Portions.Any(p => p.IsActive))
        {
            return Fail(Portions[0], "At least one portion must be on.");
        }

        var defaultRow = Portions.Single(p => p.IsDefault);
        return defaultRow.IsActive ? null : Fail(defaultRow, "The default portion must be on. Choose another default first.");
    }

    private List<SaveMenuItemPortionRequest> PortionRequests() =>
        Portions.Select((row, index) =>
        {
            FormNumbers.TryParseMoney(row.PriceText, out var price);
            decimal? quantity = !string.IsNullOrWhiteSpace(row.QuantityText) && FormNumbers.TryParseMoney(row.QuantityText, out var q) ? q : null;
            return new SaveMenuItemPortionRequest
            {
                Id = row.Id,
                Name = row.Name.Trim(),
                Quantity = quantity,
                Unit = quantity is null || string.IsNullOrWhiteSpace(row.Unit) ? null : row.Unit,
                Price = price,
                IsDefault = row.IsDefault,
                IsActive = row.IsActive,
                SortOrder = index + 1,
            };
        }).ToList();

    public CreateMenuItemRequest ToCreateRequest()
    {
        var portions = PortionRequests();
        var price = portions.FirstOrDefault(p => p.IsDefault)?.Price ?? 0m;
        FormNumbers.TryParseMoney(PackagingCharge, out var packaging);
        return new CreateMenuItemRequest
        {
            CategoryId = Category!.Id,
            Name = Name.Trim(),
            Code = string.IsNullOrWhiteSpace(Code) ? null : Code.Trim(),
            Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
            Price = price,
            PackagingCharge = packaging,
            TaxId = Tax?.Id,
            HsnSac = string.IsNullOrWhiteSpace(HsnSac) ? null : HsnSac.Trim(),
            PreparationStationId = Station!.Id,
            SortOrder = int.Parse(SortOrder),
            IsAvailable = IsAvailable,
            ModifierGroupIds = SelectedGroups(),
            Portions = portions,
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
            PackagingCharge = create.PackagingCharge,
            TaxId = create.TaxId,
            HsnSac = create.HsnSac,
            PreparationStationId = create.PreparationStationId,
            SortOrder = create.SortOrder,
            IsAvailable = create.IsAvailable,
            ModifierGroupIds = create.ModifierGroupIds,
            Portions = create.Portions,
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

public sealed partial class MenuItemsPageViewModel : ViewModelBase, IMenuAdminPage
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

    // What the list shows: the items with their portion chips and picture.
    public ObservableCollection<MenuItemRowViewModel> Rows { get; } = new();

    [ObservableProperty]
    private MenuItemRowViewModel? _selectedRow;

    partial void OnSelectedRowChanged(MenuItemRowViewModel? value) => SelectedItem = value?.Item;

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

    public bool HasSelection => SelectedItem is not null;

    public string SoldOutText => SelectedItem?.IsAvailable == false ? "Mark available" : "Mark sold out";

    public Task LoadAsync() => ExecuteAsync(async () =>
    {
        if (await FetchAsync(_menuApi.GetCategoriesAsync(includeInactive: true)) is not { } categories
            || await FetchAsync(_menuApi.GetTaxesAsync(includeInactive: false)) is not { } taxes
            || await FetchAsync(_menuApi.GetStationsAsync(includeInactive: false)) is not { } stations
            || await FetchAsync(_menuApi.GetModifierGroupsAsync(includeInactive: false)) is not { } groups)
        {
            return;
        }

        Editor.SetLookups(categories, taxes, stations, groups);
        var filterId = CategoryFilter?.Id ?? 0;
        CategoryFilters.Clear();
        CategoryFilters.Add(new CategoryDto { Id = 0, Name = "All categories", IsActive = true });
        foreach (var category in categories)
        {
            CategoryFilters.Add(category);
        }

        CategoryFilter = CategoryFilters.FirstOrDefault(c => c.Id == filterId) ?? CategoryFilters[0];
        await LoadItemsAsync();
    });

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

    // A portion chip in the list opens the item's editor with that portion's row highlighted.
    [RelayCommand]
    private async Task EditPortionAsync(PortionChip? chip)
    {
        if (chip is null || chip.ItemId == 0 || Rows.FirstOrDefault(r => r.Item.Id == chip.ItemId) is not { } row)
        {
            return;
        }

        SelectedRow = row;
        await EditAsync();
        Editor.FocusPortion(chip.PortionId);
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

        await ExecuteAsync(async () =>
        {
            var result = Editor.IsNew
                ? await _menuApi.CreateItemAsync(Editor.ToCreateRequest())
                : await _menuApi.UpdateItemAsync(Editor.ItemId, Editor.ToUpdateRequest());

            if (result.Success && result.Data is not null)
            {
                var saved = result.Data;
                if (Editor.IsNew && Editor.PendingImagePath is { } pending)
                {
                    // The picture chosen before the item existed goes up now that it has an id.
                    _notifications.Success($"{saved.Name} created.");
                    saved = await UploadPendingPictureAsync(saved, pending) ?? saved;
                }
                else
                {
                    _notifications.Success(Editor.IsNew ? $"{saved.Name} created. You can add a picture now." : $"{saved.Name} updated.");
                }

                await LoadItemsAsync(saved.Id);
                Editor.BeginEdit(saved);
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
        });
    }

    // Uploads the picture chosen on the New item form. A failure keeps the item (it is already saved) and says so.
    private async Task<MenuItemDto?> UploadPendingPictureAsync(MenuItemDto item, string path)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            var upload = await _menuApi.UploadImageAsync(item.Id, stream, Path.GetFileName(path));
            if (upload.Success && upload.Data is not null)
            {
                return upload.Data;
            }

            _notifications.Warning($"{item.Name} was saved, but its picture was not uploaded: {ApiFailures.Describe(upload)} Choose it again.");
        }
        catch (IOException ex)
        {
            _notifications.Warning($"{item.Name} was saved, but its picture could not be read: {ex.Message}");
        }

        return null;
    }

    [RelayCommand]
    private async Task ChoosePictureAsync()
    {
        if (_filePicker.PickImage() is not { } path)
        {
            return;
        }

        var info = new FileInfo(path);
        if (info.Length > MenuLimits.MaxImageBytes)
        {
            Editor.ErrorMessage = $"The picture is larger than {MenuLimits.MaxImageBytes / (1024 * 1024)} MB. Choose a smaller one.";
            return;
        }

        if (Editor.IsNew)
        {
            // Nothing to upload to yet: keep the file, show it, and upload it when the item is created.
            Editor.PendingImagePath = path;
            Editor.ImageFile = path;
            Editor.ErrorMessage = null;
            return;
        }

        await ExecuteAsync(async () =>
        {
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
        });
    }

    [RelayCommand]
    private async Task RemovePictureAsync()
    {
        if (Editor.IsNew)
        {
            Editor.PendingImagePath = null;
            Editor.ImageFile = null;
            return;
        }

        if (!Editor.HasPicture
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
        var items = await FetchAsync(_menuApi.GetItemsAsync(new MenuItemQuery
        {
            CategoryId = CategoryFilter is { Id: > 0 } filter ? filter.Id : null,
            Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
            IncludeInactive = ShowInactive,
        }));
        if (items is null)
        {
            return;
        }

        Items.Clear();
        Rows.Clear();
        foreach (var item in items)
        {
            Items.Add(item);
            Rows.Add(new MenuItemRowViewModel(item));
        }

        SelectedItem = Items.FirstOrDefault(i => i.Id == keep);
        SelectedRow = Rows.FirstOrDefault(r => r.Item.Id == keep);
        _ = LoadRowPicturesAsync(Rows.ToList());
    }

    // The thumbnails arrive one by one; a missing picture simply keeps the initials tile.
    private async Task LoadRowPicturesAsync(IReadOnlyList<MenuItemRowViewModel> rows)
    {
        foreach (var row in rows.Where(r => r.Item.ImageUrl is not null))
        {
            try
            {
                row.ImageFile = await _menuCache.GetImageFileAsync(row.Item.ImageUrl);
            }
            catch (Exception)
            {
                // No picture is not an error here.
            }
        }
    }
}
