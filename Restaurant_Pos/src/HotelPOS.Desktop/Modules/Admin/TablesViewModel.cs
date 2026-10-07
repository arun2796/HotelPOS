using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Floor;
using HotelPOS.Desktop.Modules.Tables;
using HotelPOS.Desktop.Modules.Common;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Modules.Admin;

/// <summary>A row of the admin tables grid.</summary>
public sealed record TableRow(TableDto Table)
{
    public string StatusText => TableTileViewModel.DescribeStatus(Table.Status);
}

/// <summary>Side panel used to create a table or edit the selected one.</summary>
public sealed partial class TableEditorViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(SaveText))]
    private bool _isNew;

    [ObservableProperty]
    private int _tableId;

    [ObservableProperty]
    private string _code = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private SectionDto? _section;

    [ObservableProperty]
    private string _capacity = "4";

    [ObservableProperty]
    private bool _isActive = true;

    [ObservableProperty]
    private string _rowVersion = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>Active sections a table can be placed in.</summary>
    public ObservableCollection<SectionDto> Sections { get; } = new();

    public string Title => IsNew ? "New table" : "Edit table";

    public string SaveText => IsNew ? "Create table" : "Save changes";

    public void SetSections(IEnumerable<SectionDto> sections)
    {
        var selectedId = Section?.Id;
        Sections.Clear();
        foreach (var section in sections.Where(s => s.IsActive))
        {
            Sections.Add(section);
        }

        Section = Sections.FirstOrDefault(s => s.Id == selectedId);
    }

    public void BeginCreate(int? sectionId)
    {
        IsNew = true;
        TableId = 0;
        Code = Name = RowVersion = string.Empty;
        Capacity = "4";
        IsActive = true;
        Section = Sections.FirstOrDefault(s => s.Id == sectionId) ?? Sections.FirstOrDefault();
        ErrorMessage = null;
        IsOpen = true;
    }

    public void BeginEdit(TableDto table)
    {
        IsNew = false;
        TableId = table.Id;
        Code = table.Code;
        Name = table.Name ?? string.Empty;
        Capacity = table.Capacity.ToString();
        IsActive = table.IsActive;
        RowVersion = table.RowVersion;
        Section = Sections.FirstOrDefault(s => s.Id == table.SectionId);
        ErrorMessage = null;
        IsOpen = true;
    }

    public void Close()
    {
        IsOpen = false;
        ErrorMessage = null;
    }

    /// <summary>Quick client-side checks; the server validates everything again.</summary>
    public string? Validate()
    {
        var code = Code.Trim();
        if (code.Length is 0 or > FloorLimits.CodeMaxLength || !code.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
        {
            return $"Table code must be 1 to {FloorLimits.CodeMaxLength} letters or digits, e.g. T05.";
        }

        if (Section is null)
        {
            return "Select a section.";
        }

        return int.TryParse(Capacity, out var capacity) && capacity is >= FloorLimits.MinCapacity and <= FloorLimits.MaxCapacity
            ? null
            : $"Seats must be a number from {FloorLimits.MinCapacity} to {FloorLimits.MaxCapacity}.";
    }

    public CreateTableRequest ToCreateRequest() => new()
    {
        Code = Code.Trim(),
        Name = string.IsNullOrWhiteSpace(Name) ? null : Name.Trim(),
        SectionId = Section!.Id,
        Capacity = int.Parse(Capacity),
    };

    public UpdateTableRequest ToUpdateRequest() => new()
    {
        Code = Code.Trim(),
        Name = string.IsNullOrWhiteSpace(Name) ? null : Name.Trim(),
        SectionId = Section!.Id,
        Capacity = int.Parse(Capacity),
        IsActive = IsActive,
        RowVersion = RowVersion,
    };
}

/// <summary>Admin/Manager: tables — list by section, create, edit, (de)activate, out of service.</summary>
public sealed partial class TablesViewModel : ObservableObject
{
    private readonly IFloorApi _floorApi;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;
    private List<TableDto> _all = new();

    public TablesViewModel(IFloorApi floorApi, IDialogService dialogs, INotificationService notifications)
    {
        _floorApi = floorApi;
        _dialogs = dialogs;
        _notifications = notifications;
    }

    public ObservableCollection<TableRow> Tables { get; } = new();

    /// <summary>Section filter; the first entry ("All sections") has Id 0.</summary>
    public ObservableCollection<SectionDto> SectionFilters { get; } = new();

    public TableEditorViewModel Editor { get; } = new();

    [ObservableProperty]
    private SectionDto? _sectionFilter;

    [ObservableProperty]
    private bool _showInactive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(ServiceToggleText))]
    [NotifyCanExecuteChangedFor(nameof(EditCommand), nameof(ToggleServiceCommand))]
    private TableRow? _selectedRow;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    public bool HasSelection => SelectedRow is not null;

    public string ServiceToggleText => SelectedRow?.Table.Status == TableStatus.OutOfService ? "Return to service" : "Set out of service";

    public async Task LoadAsync(int? selectId = null)
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var sections = await _floorApi.GetSectionsAsync(includeInactive: true);
            var map = await _floorApi.GetMapAsync(includeInactive: true);
            if (!sections.Success || sections.Data is null)
            {
                ErrorMessage = sections.Message;
                return;
            }

            if (!map.Success || map.Data is null)
            {
                ErrorMessage = map.Message;
                return;
            }

            Editor.SetSections(sections.Data);
            var filterId = SectionFilter?.Id ?? 0;
            SectionFilters.Clear();
            SectionFilters.Add(new SectionDto { Id = 0, Name = "All sections", IsActive = true });
            foreach (var section in sections.Data)
            {
                SectionFilters.Add(section);
            }

            _all = map.Data.Sections.SelectMany(s => s.Tables).ToList();
            SectionFilter = SectionFilters.FirstOrDefault(s => s.Id == filterId) ?? SectionFilters[0];
            ApplyFilter(selectId ?? SelectedRow?.Table.Id);
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSectionFilterChanged(SectionDto? value) => ApplyFilter(SelectedRow?.Table.Id);

    partial void OnShowInactiveChanged(bool value) => ApplyFilter(SelectedRow?.Table.Id);

    private void ApplyFilter(int? selectId)
    {
        var sectionId = SectionFilter?.Id ?? 0;
        Tables.Clear();
        foreach (var table in _all.Where(t => (sectionId == 0 || t.SectionId == sectionId) && (ShowInactive || t.IsActive)))
        {
            Tables.Add(new TableRow(table));
        }

        SelectedRow = Tables.FirstOrDefault(r => r.Table.Id == selectId);
    }

    [RelayCommand]
    private Task RefreshListAsync() => LoadAsync();

    [RelayCommand]
    private void NewTable()
    {
        if (Editor.Sections.Count == 0)
        {
            _notifications.Warning("Create an active section first.");
            return;
        }

        Editor.BeginCreate(SectionFilter?.Id);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Edit()
    {
        if (SelectedRow is not null)
        {
            Editor.BeginEdit(SelectedRow.Table);
        }
    }

    [RelayCommand]
    private void CancelEdit() => Editor.Close();

    [RelayCommand]
    private async Task SaveTableAsync()
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
                ? await _floorApi.CreateTableAsync(Editor.ToCreateRequest())
                : await _floorApi.UpdateTableAsync(Editor.TableId, Editor.ToUpdateRequest());

            if (result.Success && result.Data is not null)
            {
                _notifications.Success(Editor.IsNew ? $"Table {result.Data.Code} created." : $"Table {result.Data.Code} updated.");
                Editor.Close();
                await LoadAsync(result.Data.Id);
                return;
            }

            if (result.HasError(ErrorCodes.ConcurrencyConflict) && result.Data is not null)
            {
                // Someone else saved first (or the table was occupied): show their version and let the user decide again.
                Editor.BeginEdit(result.Data);
                Editor.ErrorMessage = result.Message;
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

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task ToggleServiceAsync()
    {
        var table = SelectedRow?.Table;
        if (table is null)
        {
            return;
        }

        var outOfService = table.Status != TableStatus.OutOfService;
        if (outOfService && !await _dialogs.ConfirmAsync(
                "Set out of service",
                $"Table {table.Code} will be shown in grey and cannot be occupied until it is returned to service.",
                "Set out of service"))
        {
            return;
        }

        var result = await _floorApi.SetOutOfServiceAsync(table.Id, outOfService);
        if (result.Success && result.Data is not null)
        {
            _notifications.Success(outOfService ? $"Table {table.Code} is out of service." : $"Table {table.Code} is back in service.");
            await LoadAsync(table.Id);
        }
        else
        {
            _notifications.Error(ApiFailures.Describe(result), result.CorrelationId);
            await LoadAsync(table.Id);
        }
    }
}
