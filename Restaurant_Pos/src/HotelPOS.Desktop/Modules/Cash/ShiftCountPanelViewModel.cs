using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Cash;

namespace HotelPOS.Desktop.Modules.Cash;

public enum ShiftCountMode
{
    Open,
    Close,
    HandOver,
    ForceClose,
}

// The counting dialog: open (the float), close, handover (count and the next cashier) and a manager's force close.
public sealed partial class ShiftCountPanelViewModel : ObservableObject
{
    private readonly Func<ShiftCountPanelViewModel, Task> _submit;
    private readonly Action _cancel;

    public ShiftCountPanelViewModel(
        ShiftCountMode mode,
        DenominationSheetViewModel sheet,
        string subtitle,
        Func<ShiftCountPanelViewModel, Task> submit,
        Action cancel,
        IEnumerable<CashierDto>? cashiers = null)
    {
        Mode = mode;
        Sheet = sheet;
        Subtitle = subtitle;
        _submit = submit;
        _cancel = cancel;
        foreach (var cashier in cashiers ?? Array.Empty<CashierDto>())
        {
            Cashiers.Add(cashier);
        }

        Sheet.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DenominationSheetViewModel.HasVariance))
            {
                OnPropertyChanged(nameof(NeedsReason));
            }
        };
    }

    public ShiftCountMode Mode { get; }

    public DenominationSheetViewModel Sheet { get; }

    public string Subtitle { get; }

    public string Title => Mode switch
    {
        ShiftCountMode.Open => "Open shift",
        ShiftCountMode.Close => "Close shift",
        ShiftCountMode.HandOver => "Hand over shift",
        _ => "Force close shift",
    };

    public string ConfirmText => Mode switch
    {
        ShiftCountMode.Open => "OPEN SHIFT",
        ShiftCountMode.Close => "CLOSE SHIFT",
        ShiftCountMode.HandOver => "HAND OVER",
        _ => "FORCE CLOSE",
    };

    public string CountCaption => Mode == ShiftCountMode.Open
        ? "Count the float in the drawer."
        : Sheet.IsBlind ? "Count the drawer. The expected cash is shown after the count (blind count)." : "Count the drawer.";

    public bool IsHandOver => Mode == ShiftCountMode.HandOver;

    public ObservableCollection<CashierDto> Cashiers { get; } = new();

    [ObservableProperty]
    private CashierDto? _selectedCashier;

    [ObservableProperty]
    private string _reason = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsReason))]
    private bool _reasonRequested;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isBusy;

    // A force close always needs one; a count needs one when it differs (with a blind count, once the server says so).
    public bool NeedsReason => Mode == ShiftCountMode.ForceClose || (Mode != ShiftCountMode.Open && (Sheet.HasVariance || ReasonRequested));

    public string ReasonLabel => Mode == ShiftCountMode.ForceClose ? "Reason for the force close" : "Reason for the difference";

    [RelayCommand]
    private async Task ConfirmAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (IsHandOver && SelectedCashier is null)
        {
            ErrorMessage = "Choose the cashier who takes over.";
            return;
        }

        if (NeedsReason && string.IsNullOrWhiteSpace(Reason))
        {
            ErrorMessage = Mode == ShiftCountMode.ForceClose ? "Enter the reason for the force close." : "Enter the reason for the difference.";
            return;
        }

        ErrorMessage = null;
        IsBusy = true;
        try
        {
            await _submit(this);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel() => _cancel();
}
