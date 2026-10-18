using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Presentation;
using HotelPOS.Contracts.Realtime;

namespace HotelPOS.Desktop.Modules.Tables;

public sealed partial class TableTileViewModel : ObservableObject
{
    public TableTileViewModel(TableDto table, DateTime stateTimestampUtc)
    {
        Id = table.Id;
        Apply(table, stateTimestampUtc);
    }

    public int Id { get; }

    [ObservableProperty]
    private string _code = string.Empty;

    [ObservableProperty]
    private string? _name;

    [ObservableProperty]
    private int _sectionId;

    [ObservableProperty]
    private string _sectionName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GuestsText), nameof(GuestsDetailText))]
    private int _capacity;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(StatusLine), nameof(StatusIcon), nameof(IsAvailable), nameof(IsOccupied), nameof(GuestsText))]
    private TableStatus _status;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GuestsText), nameof(GuestsDetailText))]
    private int? _guestCount;

    [ObservableProperty]
    private DateTime? _occupiedAtUtc;

    [ObservableProperty]
    private int? _currentOrderId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLine))]
    private int? _currentOrderNumber;

    [ObservableProperty]
    private string _rowVersion = string.Empty;

    [ObservableProperty]
    private string _elapsedText = string.Empty;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpcomingReservation), nameof(ReservationText))]
    private HotelPOS.Contracts.Reservations.ReservationSummaryDto? _upcomingReservation;

    // Floor-plan geometry (Phase 22 P2); null until an administrator places the table.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlaced))]
    private int? _posX;

    [ObservableProperty]
    private int? _posY;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CornerRadiusValue), nameof(PlanWidth))]
    private int? _width;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CornerRadiusValue), nameof(PlanHeight))]
    private int? _height;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CornerRadiusValue))]
    private TableShape? _shape;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RotationAngle))]
    private int? _rotation;

    public bool IsPlaced => PosX is not null && PosY is not null;

    public int PlanX => PosX ?? 0;

    public int PlanY => PosY ?? 0;

    public int PlanWidth => Width ?? 120;

    public int PlanHeight => Height ?? 80;

    public double RotationAngle => Rotation ?? 0;

    public System.Windows.CornerRadius CornerRadiusValue =>
        new(Shape == TableShape.Round ? Math.Min(PlanWidth, PlanHeight) / 2.0 : 8);

    public bool HasUpcomingReservation => UpcomingReservation is not null;

    public string ReservationText => UpcomingReservation is { } res
        ? $"Reserved {res.StartTimeUtc.ToLocalTime():t} ({res.PartySize})"
        : string.Empty;

    public DateTime StateTimestampUtc { get; private set; }

    public bool IsAvailable => Status == TableStatus.Available;

    public bool IsOccupied => Status == TableStatus.Occupied;

    public string StatusText => DescribeStatus(Status);

    /// <summary>Icon name from the single status map (StatusPresentation), drawn next to the status word on the tile.</summary>
    public string StatusIcon => StatusPresentation.For(Status).Icon;

    public string StatusLine => CurrentOrderNumber is { } number ? $"{StatusText} · #{number}" : StatusText;

    public string GuestsText => GuestCount is { } guests
        ? $"{guests} / {Capacity} guests"
        : $"{Capacity} seats";

    public string GuestsDetailText => GuestCount is { } guests
        ? $"{guests} of {Capacity} seats"
        : $"None · {Capacity} seats";

    public static string DescribeStatus(TableStatus status) => status switch
    {
        TableStatus.OutOfService => "Out of service",
        _ => status.ToString(),
    };

    public void Apply(TableDto table, DateTime stateTimestampUtc)
    {
        Code = table.Code;
        Name = table.Name;
        SectionId = table.SectionId;
        SectionName = table.SectionName;
        Capacity = table.Capacity;
        Status = table.Status;
        GuestCount = table.GuestCount;
        OccupiedAtUtc = table.OccupiedAtUtc;
        CurrentOrderId = table.CurrentOrderId;
        CurrentOrderNumber = table.CurrentOrderNumber;
        UpcomingReservation = table.UpcomingReservation;
        RowVersion = table.RowVersion;
        PosX = table.PosX;
        PosY = table.PosY;
        Width = table.Width;
        Height = table.Height;
        Shape = table.Shape;
        Rotation = table.Rotation;
        OnPropertyChanged(nameof(PlanX));
        OnPropertyChanged(nameof(PlanY));
        if (stateTimestampUtc > StateTimestampUtc)
        {
            StateTimestampUtc = stateTimestampUtc;
        }
    }

    public bool Apply(TableStatusChangedEvent change)
    {
        if (change.OccurredAtUtc < StateTimestampUtc)
        {
            return false;
        }

        Status = change.Status;
        GuestCount = change.GuestCount;
        OccupiedAtUtc = change.OccupiedAtUtc;
        CurrentOrderId = change.OrderId;
        CurrentOrderNumber = change.OrderNumber;
        if (!string.IsNullOrEmpty(change.EntityVersion))
        {
            RowVersion = change.EntityVersion;
        }

        StateTimestampUtc = change.OccurredAtUtc;
        return true;
    }

    public void UpdateElapsed(DateTime serverNowUtc)
    {
        if (OccupiedAtUtc is not { } since || Status is TableStatus.Available or TableStatus.OutOfService)
        {
            ElapsedText = string.Empty;
            return;
        }

        var minutes = Math.Max(0, (int)(serverNowUtc - since).TotalMinutes);
        ElapsedText = minutes < 60
            ? $"{minutes} min"
            : string.Create(CultureInfo.CurrentCulture, $"{minutes / 60} h {minutes % 60:00} min");
    }
}
