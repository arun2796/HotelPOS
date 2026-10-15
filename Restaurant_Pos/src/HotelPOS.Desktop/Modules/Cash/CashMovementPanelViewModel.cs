using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Enums;
using HotelPOS.Desktop.Modules.Billing;
using HotelPOS.Desktop.Services.Printing;

namespace HotelPOS.Desktop.Modules.Cash;

public sealed record MovementTypeOption(CashMovementType Type, string Title, string Hint);

// Pay-in, pay-out or till voucher with a reason; money out above the limit asks for a manager.
public sealed partial class CashMovementPanelViewModel : ObservableObject
{
    private readonly Func<CashMovementPanelViewModel, Task> _submit;
    private readonly Action _cancel;

    public CashMovementPanelViewModel(Func<CashMovementPanelViewModel, Task> submit, Action cancel)
    {
        _submit = submit;
        _cancel = cancel;
        _selectedType = Types[1];
    }

    public IReadOnlyList<MovementTypeOption> Types { get; } = new[]
    {
        new MovementTypeOption(CashMovementType.PayIn, ShiftReportLayout.Label(CashMovementType.PayIn), "Cash put into the drawer (change from the bank)."),
        new MovementTypeOption(CashMovementType.PayOut, ShiftReportLayout.Label(CashMovementType.PayOut), "Cash paid out (a supplier, vegetables)."),
        new MovementTypeOption(CashMovementType.TillVoucher, ShiftReportLayout.Label(CashMovementType.TillVoucher), "Small expense against a voucher (auto fare, tea)."),
    };

    [ObservableProperty]
    private MovementTypeOption _selectedType;

    [ObservableProperty]
    private string _amountText = string.Empty;

    [ObservableProperty]
    private string _reason = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isBusy;

    public decimal Amount => Money.TryParse(AmountText, out var amount) ? amount : 0m;

    [RelayCommand]
    private async Task ConfirmAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (!Money.TryParse(AmountText, out var amount) || amount <= 0)
        {
            ErrorMessage = "Enter an amount above zero with at most two decimals.";
            return;
        }

        if (string.IsNullOrWhiteSpace(Reason))
        {
            ErrorMessage = "Enter the reason for the cash movement.";
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
