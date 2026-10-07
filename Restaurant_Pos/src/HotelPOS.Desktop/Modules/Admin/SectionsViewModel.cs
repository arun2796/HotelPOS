using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Floor;
using HotelPOS.Desktop.Modules.Common;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Modules.Admin;

public sealed partial class SectionEditorViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(SaveText))]
    private bool _isNew;

    [ObservableProperty]
    private int _sectionId;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _sortOrder = "0";

    [ObservableProperty]
    private bool _isActive = true;

    [ObservableProperty]
    private string? _errorMessage;

    public string Title => IsNew ? "New section" : "Edit section";

    public string SaveText => IsNew ? "Create section" : "Save changes";

    public void BeginCreate(int nextSortOrder)
    {
        IsNew = true;
        SectionId = 0;
        Name = string.Empty;
        SortOrder = nextSortOrder.ToString();
        IsActive = true;
        ErrorMessage = null;
        IsOpen = true;
    }

    public void BeginEdit(SectionDto section)
    {
        IsNew = false;
        SectionId = section.Id;
        Name = section.Name;
        SortOrder = section.SortOrder.ToString();
        IsActive = section.IsActive;
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
            return "Enter the section name, e.g. Ground Floor.";
        }

        return int.TryParse(SortOrder, out var order) && order is >= 0 and <= FloorLimits.MaxSortOrder
            ? null
            : $"Sort order must be a number from 0 to {FloorLimits.MaxSortOrder}.";
    }

    public CreateSectionRequest ToCreateRequest() => new() { Name = Name.Trim(), SortOrder = int.Parse(SortOrder) };

    public UpdateSectionRequest ToUpdateRequest() => new() { Name = Name.Trim(), SortOrder = int.Parse(SortOrder), IsActive = IsActive };
}

public sealed partial class SectionsViewModel : ObservableObject
{
    private readonly IFloorApi _floorApi;
    private readonly INotificationService _notifications;

    public SectionsViewModel(IFloorApi floorApi, INotificationService notifications)
    {
        _floorApi = floorApi;
        _notifications = notifications;
    }

    public ObservableCollection<SectionDto> Sections { get; } = new();

    public SectionEditorViewModel Editor { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(EditCommand))]
    private SectionDto? _selectedSection;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    public bool HasSelection => SelectedSection is not null;

    public async Task LoadAsync(int? selectId = null)
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _floorApi.GetSectionsAsync(includeInactive: true);
            if (!result.Success || result.Data is null)
            {
                ErrorMessage = result.Message;
                return;
            }

            var keep = selectId ?? SelectedSection?.Id;
            Sections.Clear();
            foreach (var section in result.Data)
            {
                Sections.Add(section);
            }

            SelectedSection = Sections.FirstOrDefault(s => s.Id == keep);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task RefreshListAsync() => LoadAsync();

    [RelayCommand]
    private void NewSection() => Editor.BeginCreate(Sections.Count == 0 ? 1 : Sections.Max(s => s.SortOrder) + 1);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Edit()
    {
        if (SelectedSection is not null)
        {
            Editor.BeginEdit(SelectedSection);
        }
    }

    [RelayCommand]
    private void CancelEdit() => Editor.Close();

    [RelayCommand]
    private async Task SaveSectionAsync()
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
                ? await _floorApi.CreateSectionAsync(Editor.ToCreateRequest())
                : await _floorApi.UpdateSectionAsync(Editor.SectionId, Editor.ToUpdateRequest());

            if (result.Success && result.Data is not null)
            {
                _notifications.Success(Editor.IsNew ? $"Section {result.Data.Name} created." : $"Section {result.Data.Name} updated.");
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
}
