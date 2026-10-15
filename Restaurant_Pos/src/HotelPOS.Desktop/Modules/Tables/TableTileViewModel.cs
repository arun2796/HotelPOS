using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Floor;
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
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(StatusLine), nameof(IsAvailable), nameof(IsOccupied), nameof(GuestsText))]
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

    public DateTime StateTimestampUtc { get; private set; }

    public bool IsAvailable => Status == TableStatus.Available;

    public bool IsOccupied => Status == TableStatus.Occupied;

    public string StatusText => DescribeStatus(Status);

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
        RowVersion = table.RowVersion;
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
