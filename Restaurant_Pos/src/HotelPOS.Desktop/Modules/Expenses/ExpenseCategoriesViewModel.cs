using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Finance;
using HotelPOS.Contracts.Security;
using HotelPOS.Desktop.Modules.Common;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Auth;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Modules.Expenses;

public sealed partial class ExpenseCategoryItemViewModel : ObservableObject
{
    public ExpenseCategoryItemViewModel(ExpenseCategoryDto dto)
    {
        Dto = dto;
        Id = dto.Id;
        Name = dto.Name;
        Description = dto.Description;
        IsActive = dto.IsActive;
        SortOrder = dto.SortOrder;
    }

    public ExpenseCategoryDto Dto { get; }
    public int Id { get; }
    public string Name { get; }
    public string? Description { get; }
    public bool IsActive { get; }
    public int SortOrder { get; }

    public string StatusText => IsActive ? "Active" : "Inactive";
    public string ToggleActionText => IsActive ? "Deactivate" : "Activate";
}

public sealed partial class ExpenseCategoriesViewModel : ViewModelBase
{
    private readonly IExpensesApi _api;
    private readonly INotificationService _notifications;

    public ExpenseCategoriesViewModel(IExpensesApi api, INotificationService notifications, IAuthSession session)
    {
        _api = api;
        _notifications = notifications;

        var user = session.User;
        CanManage = user?.Roles.Contains(Roles.Admin) == true ||
                    user?.Permissions.Contains(Permissions.ExpensesManage) == true;
    }

    public bool CanManage { get; }

    public ObservableCollection<ExpenseCategoryItemViewModel> Categories { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditCommand), nameof(ToggleActiveCommand))]
    private ExpenseCategoryItemViewModel? _selectedCategory;

    [ObservableProperty]
    private bool _isEditorOpen;

    [ObservableProperty]
    private bool _isNew;

    [ObservableProperty]
    private int _editingId;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private int _sortOrder;

    [ObservableProperty]
    private string? _editorError;

    public string EditorTitle => IsNew ? "New expense category" : "Edit expense category";
    public string SaveText => IsNew ? "Create category" : "Save changes";

    [RelayCommand]
    public async Task LoadAsync()
    {
        await ExecuteAsync(async () =>
        {
            var result = await _api.ListCategoriesAsync(includeInactive: true);
            if (!result.Success || result.Data is null)
            {
                ErrorMessage = ApiFailures.Describe(result);
                return;
            }

            Categories.Clear();
            foreach (var cat in result.Data.OrderBy(c => c.SortOrder).ThenBy(c => c.Name))
            {
                Categories.Add(new ExpenseCategoryItemViewModel(cat));
            }
        });
    }

    [RelayCommand]
    private void NewCategory()
    {
        if (!CanManage) return;

        IsNew = true;
        EditingId = 0;
        Name = string.Empty;
        Description = string.Empty;
        SortOrder = Categories.Count > 0 ? Categories.Max(c => c.SortOrder) + 10 : 10;
        EditorError = null;
        IsEditorOpen = true;
    }

    [RelayCommand]
    private void Edit()
    {
        if (!CanManage || SelectedCategory is null) return;

        IsNew = false;
        EditingId = SelectedCategory.Id;
        Name = SelectedCategory.Name;
        Description = SelectedCategory.Description ?? string.Empty;
        SortOrder = SelectedCategory.SortOrder;
        EditorError = null;
        IsEditorOpen = true;
    }

    [RelayCommand]
    private void CloseEditor()
    {
        IsEditorOpen = false;
        EditorError = null;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!CanManage) return;

        var trimmedName = Name.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            EditorError = "Category name is required.";
            return;
        }

        if (trimmedName.Length > ExpenseLimits.CategoryNameMaxLength)
        {
            EditorError = $"Category name cannot exceed {ExpenseLimits.CategoryNameMaxLength} characters.";
            return;
        }

        var trimmedDesc = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();
        if (trimmedDesc?.Length > ExpenseLimits.CategoryDescriptionMaxLength)
        {
            EditorError = $"Description cannot exceed {ExpenseLimits.CategoryDescriptionMaxLength} characters.";
            return;
        }

        EditorError = null;

        if (IsNew)
        {
            var req = new CreateExpenseCategoryRequest
            {
                Name = trimmedName,
                Description = trimmedDesc,
                SortOrder = SortOrder,
            };

            var res = await _api.CreateCategoryAsync(req);
            if (!res.Success)
            {
                EditorError = ApiFailures.Describe(res);
                return;
            }

            _notifications.Success($"Category \"{trimmedName}\" created.");
        }
        else
        {
            var req = new UpdateExpenseCategoryRequest
            {
                Name = trimmedName,
                Description = trimmedDesc,
                SortOrder = SortOrder,
            };

            var res = await _api.UpdateCategoryAsync(EditingId, req);
            if (!res.Success)
            {
                EditorError = ApiFailures.Describe(res);
                return;
            }

            _notifications.Success($"Category \"{trimmedName}\" updated.");
        }

        IsEditorOpen = false;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task ToggleActiveAsync()
    {
        if (!CanManage || SelectedCategory is null) return;

        var cat = SelectedCategory;
        if (cat.IsActive)
        {
            var res = await _api.DeactivateCategoryAsync(cat.Id);
            if (!res.Success)
            {
                _notifications.Error(ApiFailures.Describe(res));
                return;
            }

            _notifications.Success($"Category \"{cat.Name}\" deactivated.");
        }
        else
        {
            var res = await _api.ActivateCategoryAsync(cat.Id);
            if (!res.Success)
            {
                _notifications.Error(ApiFailures.Describe(res));
                return;
            }

            _notifications.Success($"Category \"{cat.Name}\" activated.");
        }

        await LoadAsync();
    }
}

