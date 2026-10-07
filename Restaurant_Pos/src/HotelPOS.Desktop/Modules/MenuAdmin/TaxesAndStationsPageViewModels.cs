using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Menu;
using HotelPOS.Desktop.Modules.Common;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Modules.MenuAdmin;

public sealed partial class TaxesPageViewModel : ObservableObject, IMenuAdminPage
{
    private readonly IMenuApi _menuApi;
    private readonly INotificationService _notifications;

    public TaxesPageViewModel(IMenuApi menuApi, INotificationService notifications)
    {
        _menuApi = menuApi;
        _notifications = notifications;
    }

    public string Title => "Taxes";

    public ObservableCollection<TaxDto> Taxes { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditCommand))]
    private TaxDto? _selectedTax;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isEditorOpen;

    [ObservableProperty]
    private string _editorTitle = string.Empty;

    [ObservableProperty]
    private int _editId;

    [ObservableProperty]
    private string _editName = string.Empty;

    [ObservableProperty]
    private string _editCode = string.Empty;

    [ObservableProperty]
    private string _editRate = "5";

    [ObservableProperty]
    private bool _editIsActive = true;

    [ObservableProperty]
    private string? _editorError;

    public async Task LoadAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _menuApi.GetTaxesAsync(includeInactive: true);
            if (!result.Success || result.Data is null)
            {
                ErrorMessage = result.Message;
                return;
            }

            var keep = SelectedTax?.Id;
            Taxes.Clear();
            foreach (var tax in result.Data)
            {
                Taxes.Add(tax);
            }

            SelectedTax = Taxes.FirstOrDefault(t => t.Id == keep);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void NewTax() => Open(null);

    private bool HasSelection() => SelectedTax is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Edit() => Open(SelectedTax);

    [RelayCommand]
    private void CancelEdit() => IsEditorOpen = false;

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(EditName) || string.IsNullOrWhiteSpace(EditCode))
        {
            EditorError = "Enter a name and a code, e.g. GST 5% / GST5.";
            return;
        }

        if (!FormNumbers.TryParseMoney(EditRate, out var rate) || rate is < 0 or > 100)
        {
            EditorError = "The rate must be a percentage from 0 to 100.";
            return;
        }

        var request = new SaveTaxRequest { Name = EditName.Trim(), Code = EditCode.Trim(), RatePercent = rate, IsActive = EditIsActive };
        var result = EditId == 0 ? await _menuApi.CreateTaxAsync(request) : await _menuApi.UpdateTaxAsync(EditId, request);
        if (result.Success && result.Data is not null)
        {
            _notifications.Success($"Tax {result.Data.Name} saved.");
            IsEditorOpen = false;
            await LoadAsync();
            SelectedTax = Taxes.FirstOrDefault(t => t.Id == result.Data.Id);
            return;
        }

        EditorError = ApiFailures.Describe(result);
    }

    private void Open(TaxDto? tax)
    {
        EditorTitle = tax is null ? "New tax" : "Edit tax";
        EditId = tax?.Id ?? 0;
        EditName = tax?.Name ?? string.Empty;
        EditCode = tax?.Code ?? string.Empty;
        EditRate = tax is null ? "5" : tax.RatePercent.ToString("0.##", CultureInfo.CurrentCulture);
        EditIsActive = tax?.IsActive ?? true;
        EditorError = null;
        IsEditorOpen = true;
    }
}

public sealed partial class StationsPageViewModel : ObservableObject, IMenuAdminPage
{
    private readonly IMenuApi _menuApi;
    private readonly INotificationService _notifications;

    public StationsPageViewModel(IMenuApi menuApi, INotificationService notifications)
    {
        _menuApi = menuApi;
        _notifications = notifications;
    }

    public string Title => "Stations";

    public ObservableCollection<StationDto> Stations { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditCommand))]
    private StationDto? _selectedStation;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isEditorOpen;

    [ObservableProperty]
    private string _editorTitle = string.Empty;

    [ObservableProperty]
    private int _editId;

    [ObservableProperty]
    private string _editName = string.Empty;

    [ObservableProperty]
    private string _editCode = string.Empty;

    [ObservableProperty]
    private string _editSortOrder = "1";

    [ObservableProperty]
    private bool _editIsActive = true;

    [ObservableProperty]
    private string? _editorError;

    public async Task LoadAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _menuApi.GetStationsAsync(includeInactive: true);
            if (!result.Success || result.Data is null)
            {
                ErrorMessage = result.Message;
                return;
            }

            var keep = SelectedStation?.Id;
            Stations.Clear();
            foreach (var station in result.Data)
            {
                Stations.Add(station);
            }

            SelectedStation = Stations.FirstOrDefault(s => s.Id == keep);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void NewStation() => Open(null);

    private bool HasSelection() => SelectedStation is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Edit() => Open(SelectedStation);

    [RelayCommand]
    private void CancelEdit() => IsEditorOpen = false;

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(EditName) || string.IsNullOrWhiteSpace(EditCode))
        {
            EditorError = "Enter a name and a code, e.g. Bar / BAR.";
            return;
        }

        if (!int.TryParse(EditSortOrder, out var order) || order is < 0 or > MenuLimits.MaxSortOrder)
        {
            EditorError = $"Sort order must be a number from 0 to {MenuLimits.MaxSortOrder}.";
            return;
        }

        var request = new SaveStationRequest { Name = EditName.Trim(), Code = EditCode.Trim(), SortOrder = order, IsActive = EditIsActive };
        var result = EditId == 0 ? await _menuApi.CreateStationAsync(request) : await _menuApi.UpdateStationAsync(EditId, request);
        if (result.Success && result.Data is not null)
        {
            _notifications.Success($"Station {result.Data.Name} saved.");
            IsEditorOpen = false;
            await LoadAsync();
            SelectedStation = Stations.FirstOrDefault(s => s.Id == result.Data.Id);
            return;
        }

        EditorError = ApiFailures.Describe(result);
    }

    private void Open(StationDto? station)
    {
        EditorTitle = station is null ? "New station" : "Edit station";
        EditId = station?.Id ?? 0;
        EditName = station?.Name ?? string.Empty;
        EditCode = station?.Code ?? string.Empty;
        EditSortOrder = FormNumbers.Integer(station?.SortOrder ?? (Stations.Count == 0 ? 1 : Stations.Max(s => s.SortOrder) + 1));
        EditIsActive = station?.IsActive ?? true;
        EditorError = null;
        IsEditorOpen = true;
    }
}
