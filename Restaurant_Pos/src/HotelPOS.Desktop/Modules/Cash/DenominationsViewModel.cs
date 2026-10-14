using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Cash;
using HotelPOS.Contracts.Enums;
using HotelPOS.Desktop.Modules.Billing;
using HotelPOS.Desktop.Modules.Common;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Modules.Cash;

public sealed partial class DenominationEditRow : ObservableObject
{
    public DenominationEditRow(DenominationDto? source = null)
    {
        Id = source?.Id;
        _label = source?.Label ?? string.Empty;
        _valueText = source is null ? string.Empty : source.Value.ToString("0.##", CultureInfo.CurrentCulture);
        _isCoin = source?.Kind == DenominationKind.Coin;
        _sortOrder = source?.SortOrder ?? 0;
        _isActive = source?.IsActive ?? true;
    }

    public int? Id { get; }

    [ObservableProperty]
    private string _label;

    [ObservableProperty]
    private string _valueText;

    [ObservableProperty]
    private bool _isCoin;

    [ObservableProperty]
    private int _sortOrder;

    [ObservableProperty]
    private bool _isActive;
}

// The Denominations tab of Billing setup: the notes and coins counted in the drawer. Admins edit; others read.
public sealed partial class DenominationsViewModel : ViewModelBase
{
    private readonly ICashApi _cashApi;
    private readonly INotificationService _notifications;

    public DenominationsViewModel(ICashApi cashApi, INotificationService notifications, bool canEdit)
    {
        _cashApi = cashApi;
        _notifications = notifications;
        CanEdit = canEdit;
    }

    public ObservableCollection<DenominationEditRow> Rows { get; } = new();

    public bool CanEdit { get; }

    public bool IsReadOnly => !CanEdit;

    public async Task LoadAsync()
    {
        var list = await FetchAsync(_cashApi.GetDenominationsAsync());
        if (list is null)
        {
            return;
        }

        ErrorMessage = null;
        Rows.Clear();
        foreach (var denomination in list)
        {
            Rows.Add(new DenominationEditRow(denomination));
        }
    }

    [RelayCommand]
    private void Add() => Rows.Add(new DenominationEditRow { SortOrder = Rows.Count == 0 ? 1 : Rows.Max(r => r.SortOrder) + 1 });

    [RelayCommand]
    private async Task SaveAsync()
    {
        var items = new List<DenominationInput>();
        foreach (var row in Rows)
        {
            if (!Money.TryParse(row.ValueText, out var value) || value <= 0 || string.IsNullOrWhiteSpace(row.Label))
            {
                ErrorMessage = "Each denomination needs a label and a value above zero.";
                return;
            }

            items.Add(new DenominationInput
            {
                Id = row.Id,
                Value = value,
                Label = row.Label.Trim(),
                Kind = row.IsCoin ? DenominationKind.Coin : DenominationKind.Note,
                SortOrder = row.SortOrder,
                IsActive = row.IsActive,
            });
        }

        await ExecuteAsync(async () =>
        {
            var saved = await _cashApi.SaveDenominationsAsync(new SaveDenominationsRequest { Items = items });
            if (!saved.Success)
            {
                ErrorMessage = ApiFailures.Describe(saved);
                return;
            }

            _notifications.Success("Denominations saved.");
            await LoadAsync();
        });
    }
}
