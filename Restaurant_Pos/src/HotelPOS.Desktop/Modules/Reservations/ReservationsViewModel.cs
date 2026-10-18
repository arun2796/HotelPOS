using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Presentation;
using HotelPOS.Contracts.Reservations;
using HotelPOS.Contracts.Security;
using HotelPOS.Desktop.Modules.Common;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Auth;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Modules.Reservations;

public sealed record TableChoice(int Id, string Display);

public sealed record ReservationStatusFilter(ReservationStatus? Status, string Name);

public sealed partial class ReservationItemViewModel : ObservableObject
{
    public ReservationItemViewModel(ReservationDto dto)
    {
        Id = dto.Id;
        TableId = dto.TableId;
        TableCode = dto.TableCode;
        SectionName = dto.SectionName;
        CustomerName = dto.CustomerName;
        CustomerPhone = dto.CustomerPhone;
        PartySize = dto.PartySize;
        StartTimeUtc = dto.StartTimeUtc;
        EndTimeUtc = dto.EndTimeUtc;
        Status = dto.Status;
        Notes = dto.Notes;
        RowVersion = dto.RowVersion;
        Dto = dto;
    }

    public int Id { get; }
    public int TableId { get; }
    public string TableCode { get; }
    public string SectionName { get; }
    public string CustomerName { get; }
    public string CustomerPhone { get; }
    public int PartySize { get; }
    public DateTime StartTimeUtc { get; }
    public DateTime EndTimeUtc { get; }
    public ReservationStatus Status { get; }
    public string? Notes { get; }
    public string RowVersion { get; }
    public ReservationDto Dto { get; }

    public string TimeText => $"{StartTimeUtc.ToLocalTime():HH:mm} – {EndTimeUtc.ToLocalTime():HH:mm}";
    public string DateText => StartTimeUtc.ToLocalTime().ToString("ddd, dd MMM yyyy", CultureInfo.CurrentCulture);
    public string StatusText => StatusPresentation.For(Status).Label;
    public string StatusIcon => StatusPresentation.For(Status).Icon;
    public string GuestsText => $"{PartySize} guests";
    public string TableText => string.IsNullOrWhiteSpace(SectionName) ? TableCode : $"{TableCode} ({SectionName})";

    public bool CanSeat => Status == ReservationStatus.Booked;
    public bool CanCancel => Status == ReservationStatus.Booked;
    public bool CanMarkNoShow => Status == ReservationStatus.Booked;
    public bool CanEdit => Status == ReservationStatus.Booked;
}

public sealed partial class ReservationEditorViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(SaveText))]
    private bool _isNew;

    [ObservableProperty]
    private int _reservationId;

    [ObservableProperty]
    private string _rowVersion = string.Empty;

    [ObservableProperty]
    private ObservableCollection<TableChoice> _tableChoices = new();

    [ObservableProperty]
    private TableChoice? _selectedTable;

    [ObservableProperty]
    private string _customerName = string.Empty;

    [ObservableProperty]
    private string _customerPhone = string.Empty;

    [ObservableProperty]
    private int _partySize = 2;

    [ObservableProperty]
    private DateTime? _date = DateTime.Today;

    [ObservableProperty]
    private string _startTime = "19:00";

    [ObservableProperty]
    private string _endTime = "20:30";

    [ObservableProperty]
    private string _notes = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    public string Title => IsNew ? "New reservation" : "Edit reservation";
    public string SaveText => IsNew ? "Create reservation" : "Save changes";

    public void BeginCreate(IReadOnlyList<TableChoice> tables, DateTime date)
    {
        IsNew = true;
        ReservationId = 0;
        RowVersion = string.Empty;
        TableChoices = new ObservableCollection<TableChoice>(tables);
        SelectedTable = TableChoices.FirstOrDefault();
        CustomerName = string.Empty;
        CustomerPhone = string.Empty;
        PartySize = 2;
        Date = date;
        StartTime = "19:00";
        EndTime = "20:30";
        Notes = string.Empty;
        ErrorMessage = null;
        IsOpen = true;
    }

    public void BeginEdit(ReservationDto r, IReadOnlyList<TableChoice> tables)
    {
        IsNew = false;
        ReservationId = r.Id;
        RowVersion = r.RowVersion;
        TableChoices = new ObservableCollection<TableChoice>(tables);
        SelectedTable = TableChoices.FirstOrDefault(t => t.Id == r.TableId) ?? TableChoices.FirstOrDefault();
        CustomerName = r.CustomerName;
        CustomerPhone = r.CustomerPhone;
        PartySize = r.PartySize;
        Date = r.StartTimeUtc.ToLocalTime().Date;
        StartTime = r.StartTimeUtc.ToLocalTime().ToString("HH:mm");
        EndTime = r.EndTimeUtc.ToLocalTime().ToString("HH:mm");
        Notes = r.Notes ?? string.Empty;
        ErrorMessage = null;
        IsOpen = true;
    }

    public void Close()
    {
        IsOpen = false;
        ErrorMessage = null;
    }

    public string? Validate(out DateTime startUtc, out DateTime endUtc)
    {
        startUtc = default;
        endUtc = default;

        if (SelectedTable is null)
        {
            return "Please select a table.";
        }

        if (string.IsNullOrWhiteSpace(CustomerName))
        {
            return "Enter customer name.";
        }

        if (string.IsNullOrWhiteSpace(CustomerPhone))
        {
            return "Enter customer phone number.";
        }

        if (PartySize < 1)
        {
            return "Party size must be at least 1.";
        }

        if (!Date.HasValue)
        {
            return "Please select a date.";
        }

        if (!TimeSpan.TryParse(StartTime, out var startTs))
        {
            return "Enter a valid start time (e.g. 19:00).";
        }

        if (!TimeSpan.TryParse(EndTime, out var endTs))
        {
            return "Enter a valid end time (e.g. 20:30).";
        }

        if (endTs <= startTs)
        {
            return "End time must be after start time.";
        }

        var localStart = Date.Value.Date + startTs;
        var localEnd = Date.Value.Date + endTs;
        startUtc = DateTime.SpecifyKind(localStart, DateTimeKind.Local).ToUniversalTime();
        endUtc = DateTime.SpecifyKind(localEnd, DateTimeKind.Local).ToUniversalTime();
        return null;
    }
}

public sealed partial class ReservationsViewModel : ViewModelBase, INavigationAware, IRefreshable
{
    private readonly IReservationsApi _api;
    private readonly IFloorApi _floor;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;
    private readonly List<TableChoice> _allTableChoices = new();

    public ReservationsViewModel(
        IReservationsApi api,
        IFloorApi floor,
        IDialogService dialogs,
        INotificationService notifications,
        IAuthSession session)
    {
        _api = api;
        _floor = floor;
        _dialogs = dialogs;
        _notifications = notifications;

        var roles = session.User?.Roles ?? Array.Empty<string>();
        CanManage = roles.Contains(Roles.Admin) || roles.Contains(Roles.Manager) || roles.Contains(Roles.Waiter) || roles.Contains(Roles.Cashier);

        StatusFilters.Add(new ReservationStatusFilter(null, "All"));
        StatusFilters.Add(new ReservationStatusFilter(ReservationStatus.Booked, "Booked"));
        StatusFilters.Add(new ReservationStatusFilter(ReservationStatus.Seated, "Seated"));
        StatusFilters.Add(new ReservationStatusFilter(ReservationStatus.Cancelled, "Cancelled"));
        StatusFilters.Add(new ReservationStatusFilter(ReservationStatus.NoShow, "No Show"));
        _selectedFilter = StatusFilters[0];
    }

    public bool CanManage { get; }

    public ObservableCollection<ReservationItemViewModel> Reservations { get; } = new();

    public ObservableCollection<ReservationStatusFilter> StatusFilters { get; } = new();

    public ReservationEditorViewModel Editor { get; } = new();

    [ObservableProperty]
    private DateTime _selectedDate = DateTime.Today;

    [ObservableProperty]
    private ReservationStatusFilter? _selectedFilter;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(SeatCommand), nameof(CancelCommand), nameof(MarkNoShowCommand), nameof(EditCommand))]
    private ReservationItemViewModel? _selectedReservation;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    public bool HasSelection => SelectedReservation is not null;

    partial void OnSelectedDateChanged(DateTime value) => _ = LoadAsync();

    partial void OnSelectedFilterChanged(ReservationStatusFilter? value) => _ = LoadAsync();

    public Task OnNavigatedToAsync(object? parameter) => LoadAsync();

    public void OnNavigatedFrom() { }

    public Task RefreshAsync() => LoadAsync();

    [RelayCommand]
    private void Today() => SelectedDate = DateTime.Today;

    [RelayCommand]
    private void Tomorrow() => SelectedDate = DateTime.Today.AddDays(1);

    [RelayCommand]
    private async Task SearchAsync() => await LoadAsync();

    [RelayCommand]
    private async Task RefreshListAsync() => await LoadAsync();

    public async Task LoadAsync()
    {
        await ExecuteAsync(async () =>
        {
            // Ensure table choices are loaded
            if (_allTableChoices.Count == 0)
            {
                var floorResult = await _floor.GetMapAsync();
                if (floorResult.Success && floorResult.Data is not null)
                {
                    _allTableChoices.Clear();
                    foreach (var s in floorResult.Data.Sections.Where(s => s.IsActive))
                    {
                        foreach (var t in s.Tables.Where(t => t.IsActive))
                        {
                            _allTableChoices.Add(new TableChoice(t.Id, $"{t.Code} ({s.Name}, cap {t.Capacity})"));
                        }
                    }
                }
            }

            var fromUtc = DateTime.SpecifyKind(SelectedDate.Date, DateTimeKind.Local).ToUniversalTime();
            var toUtc = fromUtc.AddDays(1);

            var query = new ReservationQuery
            {
                FromUtc = fromUtc,
                ToUtc = toUtc,
                Status = SelectedFilter?.Status,
                Search = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
                Page = 1,
                PageSize = 200,
            };

            var result = await _api.ListAsync(query);
            if (!result.Success || result.Data is null)
            {
                ErrorMessage = result.Message;
                Reservations.Clear();
                SummaryText = string.Empty;
                return;
            }

            ErrorMessage = null;
            Reservations.Clear();
            foreach (var r in result.Data.Items.OrderBy(r => r.StartTimeUtc))
            {
                Reservations.Add(new ReservationItemViewModel(r));
            }

            var booked = Reservations.Count(r => r.Status == ReservationStatus.Booked);
            var seated = Reservations.Count(r => r.Status == ReservationStatus.Seated);
            SummaryText = $"{Reservations.Count} reservations · {booked} booked · {seated} seated";
        });
    }

    [RelayCommand]
    private void NewReservation()
    {
        Editor.BeginCreate(_allTableChoices, SelectedDate);
    }

    private bool CanEdit() => SelectedReservation?.CanEdit == true;

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void Edit()
    {
        if (SelectedReservation is null) return;
        Editor.BeginEdit(SelectedReservation.Dto, _allTableChoices);
    }

    [RelayCommand]
    private void CancelEdit() => Editor.Close();

    [RelayCommand]
    private async Task SaveReservationAsync()
    {
        var error = Editor.Validate(out var startUtc, out var endUtc);
        if (error is not null)
        {
            Editor.ErrorMessage = error;
            return;
        }

        await ExecuteAsync(async () =>
        {
            if (Editor.IsNew)
            {
                var req = new CreateReservationRequest
                {
                    TableId = Editor.SelectedTable!.Id,
                    CustomerName = Editor.CustomerName.Trim(),
                    CustomerPhone = Editor.CustomerPhone.Trim(),
                    PartySize = Editor.PartySize,
                    StartTimeUtc = startUtc,
                    EndTimeUtc = endUtc,
                    Notes = string.IsNullOrWhiteSpace(Editor.Notes) ? null : Editor.Notes.Trim(),
                };

                var res = await _api.CreateAsync(req);
                if (res.Success)
                {
                    Editor.Close();
                    _notifications.Success($"Reservation created for {req.CustomerName}.");
                    await LoadAsync();
                }
                else
                {
                    Editor.ErrorMessage = res.Message;
                }
            }
            else
            {
                var req = new UpdateReservationRequest
                {
                    TableId = Editor.SelectedTable!.Id,
                    CustomerName = Editor.CustomerName.Trim(),
                    CustomerPhone = Editor.CustomerPhone.Trim(),
                    PartySize = Editor.PartySize,
                    StartTimeUtc = startUtc,
                    EndTimeUtc = endUtc,
                    Notes = string.IsNullOrWhiteSpace(Editor.Notes) ? null : Editor.Notes.Trim(),
                    RowVersion = Editor.RowVersion,
                };

                var res = await _api.UpdateAsync(Editor.ReservationId, req);
                if (res.Success)
                {
                    Editor.Close();
                    _notifications.Success("Reservation updated.");
                    await LoadAsync();
                }
                else
                {
                    Editor.ErrorMessage = res.Message;
                }
            }
        });
    }

    private bool CanSeat() => SelectedReservation?.CanSeat == true;

    [RelayCommand(CanExecute = nameof(CanSeat))]
    private async Task SeatAsync(ReservationItemViewModel? item)
    {
        var target = item ?? SelectedReservation;
        if (target is null) return;

        await ExecuteAsync(async () =>
        {
            var result = await _api.SeatAsync(target.Id, new SeatReservationRequest { RowVersion = target.RowVersion });
            if (!result.Success)
            {
                _notifications.Error(result.Message);
                return;
            }

            // Also attempt to occupy table if currently available
            var map = await _floor.GetMapAsync();
            if (map.Success && map.Data is not null)
            {
                var table = map.Data.Sections.SelectMany(s => s.Tables).FirstOrDefault(t => t.Id == target.TableId);
                if (table is { Status: TableStatus.Available })
                {
                    var occupy = await _floor.OccupyAsync(table.Id, new OccupyTableRequest
                    {
                        GuestCount = target.PartySize,
                        RowVersion = table.RowVersion,
                    });
                    if (occupy.Success)
                    {
                        _notifications.Success($"Reservation for {target.CustomerName} seated; table {target.TableCode} occupied.");
                        await LoadAsync();
                        return;
                    }
                }
            }

            _notifications.Success($"Reservation for {target.CustomerName} marked as seated.");
            await LoadAsync();
        });
    }

    private bool CanCancel() => SelectedReservation?.CanCancel == true;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private async Task CancelAsync(ReservationItemViewModel? item)
    {
        var target = item ?? SelectedReservation;
        if (target is null) return;

        var reason = await _dialogs.PromptAsync("Cancel reservation", $"Enter cancellation reason for {target.CustomerName}:", "Cancel Reservation");
        if (reason is null) return;

        await ExecuteAsync(async () =>
        {
            var result = await _api.CancelAsync(target.Id, new CancelReservationRequest
            {
                Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
                RowVersion = target.RowVersion,
            });

            if (result.Success)
            {
                _notifications.Success($"Reservation for {target.CustomerName} cancelled.");
                await LoadAsync();
            }
            else
            {
                _notifications.Error(result.Message);
            }
        });
    }

    private bool CanMarkNoShow() => SelectedReservation?.CanMarkNoShow == true;

    [RelayCommand(CanExecute = nameof(CanMarkNoShow))]
    private async Task MarkNoShowAsync(ReservationItemViewModel? item)
    {
        var target = item ?? SelectedReservation;
        if (target is null) return;

        if (!await _dialogs.ConfirmAsync("Mark No-Show", $"Mark reservation for {target.CustomerName} at {target.TimeText} as no-show?", "Mark No-Show", destructive: true))
        {
            return;
        }

        await ExecuteAsync(async () =>
        {
            var result = await _api.MarkNoShowAsync(target.Id, new MarkNoShowRequest { RowVersion = target.RowVersion });
            if (result.Success)
            {
                _notifications.Success($"Reservation for {target.CustomerName} marked as no-show.");
                await LoadAsync();
            }
            else
            {
                _notifications.Error(result.Message);
            }
        });
    }
}
