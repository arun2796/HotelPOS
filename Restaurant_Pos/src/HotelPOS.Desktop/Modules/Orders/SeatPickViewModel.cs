using CommunityToolkit.Mvvm.ComponentModel;

namespace HotelPOS.Desktop.Modules.Orders;

// One seat chip of the order builder. Free: nobody has ordered for it. Taken: it belongs to a seat group that already has
// dishes (tap it to add more; a chair of an older multi-seat group adds to that whole group). Selected: the next dishes are for it.
public sealed partial class SeatPickViewModel : ObservableObject
{
    public SeatPickViewModel(string code)
    {
        Code = code;
    }

    public string Code { get; }

    // The seat letter on the chip ("A" of "T01A"); a taken seat carries a dot.
    public string Label => Code.Length > 0 ? Code[^1].ToString() + (IsTaken ? " ●" : string.Empty) : Code;

    public string ToolTipText => OwnerGroup is { } group
        ? group == Code
            ? $"Seat {Code} · already has dishes. Tap to add more."
            : $"Seat {Code} · already has dishes with the group {group}. Tap to add to that group."
        : IsOn ? $"Seat {Code} · selected" : $"Seat {Code} · free";

    // The seat group that already has dishes for this seat ("T01A+B"); null while the seat is free.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTaken), nameof(Label), nameof(ToolTipText))]
    private string? _ownerGroup;

    public bool IsTaken => OwnerGroup is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToolTipText))]
    private bool _isOn;
}
