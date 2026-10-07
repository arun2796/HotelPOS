using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Menu;
using HotelPOS.Desktop.Modules.Common;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Modules.MenuAdmin;

/// <summary>A row of the modifier groups grid.</summary>
public sealed record ModifierGroupRow(ModifierGroupDto Group)
{
    public string SelectionText => Group.MinSelections == 0
        ? $"Optional, up to {Group.MaxSelections}"
        : Group.MinSelections == Group.MaxSelections ? $"Exactly {Group.MinSelections}" : $"{Group.MinSelections} to {Group.MaxSelections}";

    public int OptionCount => Group.Options.Count(o => o.IsActive);
}

/// <summary>
/// Modifier groups on the left, options of the selected group below; one side panel edits either a group or an option.
/// </summary>
public sealed partial class ModifiersPageViewModel : ObservableObject, IMenuAdminPage
{
    private readonly IMenuApi _menuApi;
    private readonly INotificationService _notifications;

    public ModifiersPageViewModel(IMenuApi menuApi, INotificationService notifications)
    {
        _menuApi = menuApi;
        _notifications = notifications;
    }

    public string Title => "Modifiers";

    public ObservableCollection<ModifierGroupRow> Groups { get; } = new();

    public ObservableCollection<ModifierOptionDto> Options { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGroup))]
    [NotifyCanExecuteChangedFor(nameof(EditGroupCommand), nameof(NewOptionCommand))]
    private ModifierGroupRow? _selectedGroup;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditOptionCommand))]
    private ModifierOptionDto? _selectedOption;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    // ----- Editor (group or option) -----

    [ObservableProperty]
    private bool _isEditorOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditingGroup))]
    private bool _isEditingOption;

    [ObservableProperty]
    private string _editorTitle = string.Empty;

    [ObservableProperty]
    private int _editId;

    [ObservableProperty]
    private string _editName = string.Empty;

    [ObservableProperty]
    private string _editMin = "0";

    [ObservableProperty]
    private string _editMax = "1";

    [ObservableProperty]
    private string _editPriceDelta = "0";

    [ObservableProperty]
    private string _editSortOrder = "0";

    [ObservableProperty]
    private bool _editIsActive = true;

    [ObservableProperty]
    private string? _editorError;

    public bool HasGroup => SelectedGroup is not null;

    public bool IsEditingGroup => !IsEditingOption;

    public Task LoadAsync() => LoadAsync(null);

    partial void OnSelectedGroupChanged(ModifierGroupRow? value)
    {
        Options.Clear();
        foreach (var option in value?.Group.Options ?? Array.Empty<ModifierOptionDto>())
        {
            Options.Add(option);
        }
    }

    [RelayCommand]
    private Task RefreshListAsync() => LoadAsync(null);

    [RelayCommand]
    private void NewGroup() => OpenGroupEditor(null);

    [RelayCommand(CanExecute = nameof(HasGroup))]
    private void EditGroup() => OpenGroupEditor(SelectedGroup?.Group);

    private bool CanAddOption() => HasGroup;

    [RelayCommand(CanExecute = nameof(CanAddOption))]
    private void NewOption() => OpenOptionEditor(null);

    private bool HasOption() => SelectedOption is not null;

    [RelayCommand(CanExecute = nameof(HasOption))]
    private void EditOption() => OpenOptionEditor(SelectedOption);

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditorOpen = false;
        EditorError = null;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        EditorError = IsEditingOption ? ValidateOption() : ValidateGroup();
        if (EditorError is not null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            ApiResult<ModifierGroupDto> result;
            if (IsEditingOption)
            {
                FormNumbers.TryParseMoney(EditPriceDelta, out var delta);
                var request = new SaveModifierOptionRequest { Name = EditName.Trim(), PriceDelta = delta, SortOrder = int.Parse(EditSortOrder), IsActive = EditIsActive };
                var groupId = SelectedGroup!.Group.Id;
                result = EditId == 0
                    ? await _menuApi.AddOptionAsync(groupId, request)
                    : await _menuApi.UpdateOptionAsync(groupId, EditId, request);
            }
            else
            {
                var request = new SaveModifierGroupRequest { Name = EditName.Trim(), MinSelections = int.Parse(EditMin), MaxSelections = int.Parse(EditMax), IsActive = EditIsActive };
                result = EditId == 0
                    ? await _menuApi.CreateModifierGroupAsync(request)
                    : await _menuApi.UpdateModifierGroupAsync(EditId, request);
            }

            if (result.Success && result.Data is not null)
            {
                _notifications.Success(IsEditingOption ? $"Option {EditName.Trim()} saved." : $"Modifier group {result.Data.Name} saved.");
                IsEditorOpen = false;
                await LoadAsync(result.Data.Id);
                return;
            }

            EditorError = ApiFailures.Describe(result);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OpenGroupEditor(ModifierGroupDto? group)
    {
        IsEditingOption = false;
        EditorTitle = group is null ? "New modifier group" : "Edit modifier group";
        EditId = group?.Id ?? 0;
        EditName = group?.Name ?? string.Empty;
        EditMin = FormNumbers.Integer(group?.MinSelections ?? 0);
        EditMax = FormNumbers.Integer(group?.MaxSelections ?? 1);
        EditIsActive = group?.IsActive ?? true;
        EditorError = null;
        IsEditorOpen = true;
    }

    private void OpenOptionEditor(ModifierOptionDto? option)
    {
        IsEditingOption = true;
        EditorTitle = option is null ? $"New option in {SelectedGroup?.Group.Name}" : "Edit option";
        EditId = option?.Id ?? 0;
        EditName = option?.Name ?? string.Empty;
        EditPriceDelta = FormNumbers.Money(option?.PriceDelta ?? 0m);
        EditSortOrder = FormNumbers.Integer(option?.SortOrder ?? (Options.Count == 0 ? 1 : Options.Max(o => o.SortOrder) + 1));
        EditIsActive = option?.IsActive ?? true;
        EditorError = null;
        IsEditorOpen = true;
    }

    private string? ValidateGroup()
    {
        if (string.IsNullOrWhiteSpace(EditName))
        {
            return "Enter the group name, e.g. Spice level.";
        }

        return int.TryParse(EditMin, out var min) && int.TryParse(EditMax, out var max) && min >= 0 && max >= 1 && min <= max
               && max <= MenuLimits.MaxModifierSelections
            ? null
            : $"Minimum and maximum must be numbers with 0 <= minimum <= maximum <= {MenuLimits.MaxModifierSelections}, and maximum at least 1.";
    }

    private string? ValidateOption()
    {
        if (string.IsNullOrWhiteSpace(EditName))
        {
            return "Enter the option name, e.g. Extra cheese.";
        }

        if (!FormNumbers.TryParseMoney(EditPriceDelta, out var delta) || Math.Abs(delta) > MenuLimits.MaxPriceDelta || decimal.Round(delta, 2) != delta)
        {
            return "Enter the price change, e.g. 30, 0 or -10.";
        }

        return int.TryParse(EditSortOrder, out var order) && order is >= 0 and <= MenuLimits.MaxSortOrder
            ? null
            : $"Sort order must be a number from 0 to {MenuLimits.MaxSortOrder}.";
    }

    private async Task LoadAsync(int? selectGroupId)
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _menuApi.GetModifierGroupsAsync(includeInactive: true);
            if (!result.Success || result.Data is null)
            {
                ErrorMessage = result.Message;
                return;
            }

            var keep = selectGroupId ?? SelectedGroup?.Group.Id;
            Groups.Clear();
            foreach (var group in result.Data)
            {
                Groups.Add(new ModifierGroupRow(group));
            }

            SelectedGroup = Groups.FirstOrDefault(g => g.Group.Id == keep) ?? Groups.FirstOrDefault();
        }
        finally
        {
            IsBusy = false;
        }
    }
}
