using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Menu;
using HotelPOS.Desktop.Modules.Common;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Modules.MenuAdmin;

public sealed partial class CategoryEditorViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(SaveText))]
    private bool _isNew;

    [ObservableProperty]
    private int _categoryId;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _sortOrder = "0";

    [ObservableProperty]
    private bool _isActive = true;

    [ObservableProperty]
    private string? _errorMessage;

    public string Title => IsNew ? "New category" : "Edit category";

    public string SaveText => IsNew ? "Create category" : "Save changes";

    public void BeginCreate(int nextSortOrder)
    {
        IsNew = true;
        CategoryId = 0;
        Name = string.Empty;
        SortOrder = FormNumbers.Integer(nextSortOrder);
        IsActive = true;
        ErrorMessage = null;
        IsOpen = true;
    }

    public void BeginEdit(CategoryDto category)
    {
        IsNew = false;
        CategoryId = category.Id;
        Name = category.Name;
        SortOrder = FormNumbers.Integer(category.SortOrder);
        IsActive = category.IsActive;
        ErrorMessage = null;
        IsOpen = true;
    }

    public void Close()
    {
        IsOpen = false;
        ErrorMessage = null;
    }

    public string? Validate() =>
        string.IsNullOrWhiteSpace(Name) ? "Enter the category name, e.g. Starters."
        : int.TryParse(SortOrder, out var order) && order is >= 0 and <= MenuLimits.MaxSortOrder ? null
        : $"Sort order must be a number from 0 to {MenuLimits.MaxSortOrder}.";
}

/// <summary>Menu categories: order, rename, (de)activate (optionally with their items).</summary>
public sealed partial class CategoriesPageViewModel : ObservableObject, IMenuAdminPage
{
    private readonly IMenuApi _menuApi;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;

    public CategoriesPageViewModel(IMenuApi menuApi, IDialogService dialogs, INotificationService notifications)
    {
        _menuApi = menuApi;
        _dialogs = dialogs;
        _notifications = notifications;
    }

    public string Title => "Categories";

    public ObservableCollection<CategoryDto> Categories { get; } = new();

    public CategoryEditorViewModel Editor { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(EditCommand))]
    private CategoryDto? _selectedCategory;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    public bool HasSelection => SelectedCategory is not null;

    public async Task LoadAsync() => await LoadAsync(null);

    [RelayCommand]
    private Task RefreshListAsync() => LoadAsync(null);

    [RelayCommand]
    private void NewCategory() => Editor.BeginCreate(Categories.Count == 0 ? 1 : Categories.Max(c => c.SortOrder) + 1);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Edit()
    {
        if (SelectedCategory is not null)
        {
            Editor.BeginEdit(SelectedCategory);
        }
    }

    [RelayCommand]
    private void CancelEdit() => Editor.Close();

    [RelayCommand]
    private async Task SaveCategoryAsync()
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
                ? await _menuApi.CreateCategoryAsync(new CreateCategoryRequest { Name = Editor.Name.Trim(), SortOrder = int.Parse(Editor.SortOrder) })
                : await _menuApi.UpdateCategoryAsync(Editor.CategoryId, Request(deactivateItems: false));

            // Deactivating a category that still has items: ask, then deactivate them together.
            if (!Editor.IsNew && !Editor.IsActive && result.HasError(ErrorCodes.BusinessRule)
                && await _dialogs.ConfirmAsync("Deactivate category", $"{result.Message}\n\nDeactivate the category and all its items?", "Deactivate all", destructive: true))
            {
                result = await _menuApi.UpdateCategoryAsync(Editor.CategoryId, Request(deactivateItems: true));
            }

            if (result.Success && result.Data is not null)
            {
                _notifications.Success(Editor.IsNew ? $"Category {result.Data.Name} created." : $"Category {result.Data.Name} updated.");
                Editor.Close();
                await LoadAsync(result.Data.Id);
                return;
            }

            Editor.ErrorMessage = ApiFailures.Describe(result);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private UpdateCategoryRequest Request(bool deactivateItems) => new()
    {
        Name = Editor.Name.Trim(),
        SortOrder = int.Parse(Editor.SortOrder),
        IsActive = Editor.IsActive,
        DeactivateItems = deactivateItems,
    };

    private async Task LoadAsync(int? selectId)
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _menuApi.GetCategoriesAsync(includeInactive: true);
            if (!result.Success || result.Data is null)
            {
                ErrorMessage = result.Message;
                return;
            }

            var keep = selectId ?? SelectedCategory?.Id;
            Categories.Clear();
            foreach (var category in result.Data)
            {
                Categories.Add(category);
            }

            SelectedCategory = Categories.FirstOrDefault(c => c.Id == keep);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
