using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Floor;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Modules.Tables;

public sealed partial class TableDetailsViewModel : ObservableObject
{
    public const string ChangedByAnotherTerminal = "Table was changed by another terminal. The latest status is shown.";

    private const int MaxGuestDigits = 2;

    private readonly IFloorApi _floorApi;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;
    private readonly Action<TableDto> _applyTable;
    private readonly Func<Task> _refreshMap;

    public TableDetailsViewModel(
        IFloorApi floorApi,
        IDialogService dialogs,
        INotificationService notifications,
        Action<TableDto> applyTable,
        Func<Task> refreshMap)
    {
        _floorApi = floorApi;
        _dialogs = dialogs;
        _notifications = notifications;
        _applyTable = applyTable;
        _refreshMap = refreshMap;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpen), nameof(OccupiedSinceText), nameof(OrderText))]
    [NotifyCanExecuteChangedFor(nameof(OccupyCommand), nameof(ReleaseCommand))]
    private TableTileViewModel? _table;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OccupyCommand))]
    private string _guestInput = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OccupyCommand), nameof(ReleaseCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    public bool IsOpen => Table is not null;

    public string OccupiedSinceText => Table?.OccupiedAtUtc is { } since ? since.ToLocalTime().ToString("HH:mm") : "—";

    public string OrderText => Table?.CurrentOrderNumber is { } number ? $"Order #{number}" : "No order";

    public IReadOnlyList<string> Keys { get; } = new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9" };

    public void Show(TableTileViewModel? table)
    {
        Table = table;
        GuestInput = string.Empty;
        ErrorMessage = null;
    }

    public void OnTableChanged()
    {
        OnPropertyChanged(nameof(OccupiedSinceText));
        OnPropertyChanged(nameof(OrderText));
        OccupyCommand.NotifyCanExecuteChanged();
        ReleaseCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void Close() => Table = null;

    [RelayCommand]
    private void Digit(string? digit)
    {
        if (digit is { Length: 1 } && char.IsAsciiDigit(digit[0]) && GuestInput.Length < MaxGuestDigits
            && !(GuestInput.Length == 0 && digit == "0"))
        {
            GuestInput += digit;
            ErrorMessage = null;
        }
    }

    [RelayCommand]
    private void Backspace()
    {
        if (GuestInput.Length > 0)
        {
            GuestInput = GuestInput[..^1];
        }
    }

    [RelayCommand]
    private void ClearInput() => GuestInput = string.Empty;

    private bool CanOccupy() => !IsBusy && Table?.IsAvailable == true;

    [RelayCommand(CanExecute = nameof(CanOccupy))]
    private async Task OccupyAsync()
    {
        var table = Table;
        if (table is null)
        {
            return;
        }

        if (!int.TryParse(GuestInput, out var guests) || guests is < FloorLimits.MinGuests or > FloorLimits.MaxGuests)
        {
            ErrorMessage = "Enter the number of guests on the keypad.";
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _floorApi.OccupyAsync(table.Id, new OccupyTableRequest { GuestCount = guests, RowVersion = table.RowVersion });
            if (result.Success && result.Data is not null)
            {
                _applyTable(result.Data);
                GuestInput = string.Empty;
                _notifications.Success($"Table {table.Code} occupied ({guests} guests).");
                return;
            }

            HandleFailure(result, "occupied");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanRelease() => !IsBusy && Table?.IsOccupied == true;

    [RelayCommand(CanExecute = nameof(CanRelease))]
    private async Task ReleaseAsync()
    {
        var table = Table;
        if (table is null || !await _dialogs.ConfirmAsync(
                "Release table",
                $"Release table {table.Code}? It becomes available for new guests.",
                "Release"))
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _floorApi.ReleaseAsync(table.Id);
            if (result.Success && result.Data is not null)
            {
                _applyTable(result.Data);
                _notifications.Success($"Table {table.Code} released.");
                return;
            }

            HandleFailure(result, "released");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void HandleFailure(ApiResult<TableDto> result, string action)
    {
        if (result.IsConnectionFailure)
        {
            // The request may or may not have reached the server: the map shows the truth once reconnected.
            ErrorMessage = $"Connection unavailable. The table may not have been {action}; its status will refresh when the connection is back.";
            return;
        }

        if (result.HasError(ErrorCodes.ConcurrencyConflict) || result.HasError(ErrorCodes.TableNotAvailable)
            || result.HasError(ErrorCodes.InvalidStateTransition))
        {
            if (result.Data is not null)
            {
                _applyTable(result.Data);
            }
            else
            {
                _ = _refreshMap();
            }

            ErrorMessage = ChangedByAnotherTerminal;
            return;
        }

        ErrorMessage = result.Message;
    }
}
