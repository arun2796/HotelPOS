using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Billing;

namespace HotelPOS.Desktop.Modules.Billing;

public enum PaymentMode
{
    Cash,
    Card,
    Upi,
    Split,
}

public sealed record QuickTenderOption(string Label, decimal Value);

public sealed record PaymentToSend(PaymentLineViewModel Line, int MethodId, decimal Amount, decimal? Tendered, string? Reference);

public sealed partial class PaymentLineViewModel : ObservableObject
{
    public PaymentLineViewModel(PaymentMethodDto method, decimal amount)
    {
        Method = method;
        _amountText = amount > 0 ? Money.Format(amount) : string.Empty;
    }

    public PaymentMethodDto Method { get; }

    // Kept, with the exact request, until the server answers, so a retry after a lost response cannot charge twice.
    // Any definitive answer is stored by the server under the key, so a changed attempt needs a new one.
    public Guid IdempotencyKey { get; private set; } = Guid.NewGuid();

    public AddPaymentRequest? PendingRequest { get; set; }

    public void RenewKey()
    {
        IdempotencyKey = Guid.NewGuid();
        PendingRequest = null;
    }

    [ObservableProperty]
    private string _amountText;

    [ObservableProperty]
    private string _reference = string.Empty;

    [ObservableProperty]
    private bool _isPaid;

    public decimal Amount => Money.TryParse(AmountText, out var amount) && amount > 0 ? amount : 0m;

    public bool NeedsReference => Method.RequiresReference;
}

public sealed partial class PaymentViewModel : ObservableObject
{
    private string _activeField = nameof(TenderedText);
    private PaymentLineViewModel? _singleLine;

    public PaymentViewModel(PaymentMode mode, decimal balance, IReadOnlyList<PaymentMethodDto> methods)
    {
        Mode = mode;
        Balance = balance;
        Methods = methods.Where(m => m.IsActive).ToList();
        Method = mode switch
        {
            PaymentMode.Cash => Methods.FirstOrDefault(m => m.IsCash),
            PaymentMode.Card => Methods.FirstOrDefault(m => m.Code == PaymentMethodCodes.Card),
            PaymentMode.Upi => Methods.FirstOrDefault(m => m.Code == PaymentMethodCodes.Upi),
            _ => null,
        };
        _amountText = Money.Format(balance);
        _activeField = IsCash ? nameof(TenderedText) : nameof(AmountText);
        if (mode == PaymentMode.Split)
        {
            Lines.CollectionChanged += (_, e) =>
            {
                foreach (PaymentLineViewModel line in e.NewItems ?? Array.Empty<PaymentLineViewModel>())
                {
                    line.PropertyChanged += OnLineChanged;
                }

                Revalidate();
            };
        }

        Revalidate();
    }

    public PaymentMode Mode { get; }

    public decimal Balance { get; }

    public IReadOnlyList<PaymentMethodDto> Methods { get; }

    public PaymentMethodDto? Method { get; }

    public ObservableCollection<PaymentLineViewModel> Lines { get; } = new();

    public bool IsSplit => Mode == PaymentMode.Split;

    public bool IsSingle => !IsSplit;

    public bool IsCash => Method?.IsCash == true;

    public bool NeedsReference => IsSingle && Method?.RequiresReference == true;

    public string Title => Mode switch
    {
        PaymentMode.Cash => "Cash payment",
        PaymentMode.Card => "Card payment",
        PaymentMode.Upi => "UPI payment",
        _ => "Split payment",
    };

    public string BalanceText => Money.Format(Balance);

    // Exact, then the usual notes a guest hands over: the balance rounded up to 50, 100, 500 and 2000.
    public IReadOnlyList<QuickTenderOption> QuickTenders => new[] { 50m, 100m, 500m, 2000m }
        .Select(step => Math.Ceiling(Balance / step) * step)
        .Where(v => v > Balance)
        .Distinct()
        .Select(v => new QuickTenderOption(Money.Format(v), v))
        .Prepend(new QuickTenderOption("Exact", Balance))
        .ToList();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChangeText))]
    private string _amountText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChangeText))]
    private string _tenderedText = string.Empty;

    [ObservableProperty]
    private string _reference = string.Empty;

    [ObservableProperty]
    private bool _isValid;

    [ObservableProperty]
    private string? _validationMessage;

    public decimal Amount => Money.TryParse(AmountText, out var amount) ? amount : 0m;

    public decimal Tendered => Money.TryParse(TenderedText, out var tendered) ? tendered : Amount;

    public decimal Change => IsCash ? Math.Max(0m, Tendered - Math.Min(Amount, Balance)) : 0m;

    public string ChangeText => Money.Format(Change);

    public decimal Remaining => Balance - Lines.Sum(l => l.Amount);

    public string RemainingText => Money.Format(Remaining);

    public string ActiveField => _activeField;

    public IReadOnlyList<PaymentToSend> BuildPayments() => IsSplit
        ? Lines.Where(l => !l.IsPaid).Select(l => new PaymentToSend(l, l.Method.Id, l.Amount, null, Clean(l.Reference))).ToList()
        : new[] { new PaymentToSend(SingleLine, Method!.Id, Amount, IsCash ? Tendered : null, Clean(Reference)) };

    // The single-method form is sent as one line too, so it carries the same retry key.
    private PaymentLineViewModel SingleLine => _singleLine ??= new PaymentLineViewModel(Method!, 0m);

    [RelayCommand]
    private void AddLine(PaymentMethodDto? method)
    {
        if (method is null || !IsSplit)
        {
            return;
        }

        Lines.Add(new PaymentLineViewModel(method, Math.Max(0m, Remaining)));
    }

    [RelayCommand]
    private void RemoveLine(PaymentLineViewModel? line)
    {
        if (line is { IsPaid: false })
        {
            Lines.Remove(line);
            line.PropertyChanged -= OnLineChanged;
            Revalidate();
        }
    }

    [RelayCommand]
    private void QuickTender(QuickTenderOption? option)
    {
        if (option is null)
        {
            return;
        }

        TenderedText = Money.Format(option.Value);
        _activeField = nameof(TenderedText);
    }

    [RelayCommand]
    private void Focus(string? field) => _activeField = field == nameof(AmountText) ? nameof(AmountText) : nameof(TenderedText);

    [RelayCommand]
    private void Digit(string? digit)
    {
        if (digit is null)
        {
            return;
        }

        var current = _activeField == nameof(AmountText) ? AmountText : TenderedText;
        var next = current + digit;
        if (next.Length <= 10 && (Money.TryParse(next, out _) || next.EndsWith(CultureDecimalSeparator, StringComparison.Ordinal)))
        {
            SetActive(next);
        }
    }

    [RelayCommand]
    private void Backspace()
    {
        var current = _activeField == nameof(AmountText) ? AmountText : TenderedText;
        if (current.Length > 0)
        {
            SetActive(current[..^1]);
        }
    }

    [RelayCommand]
    private void ClearField() => SetActive(string.Empty);

    partial void OnAmountTextChanged(string value) => Revalidate();

    partial void OnTenderedTextChanged(string value) => Revalidate();

    partial void OnReferenceChanged(string value) => Revalidate();

    private static string CultureDecimalSeparator => System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;

    private void SetActive(string text)
    {
        if (_activeField == nameof(AmountText))
        {
            AmountText = text;
        }
        else
        {
            TenderedText = text;
        }
    }

    private void OnLineChanged(object? sender, PropertyChangedEventArgs e) => Revalidate();

    private void Revalidate()
    {
        ValidationMessage = Validate();
        IsValid = ValidationMessage is null;
        OnPropertyChanged(nameof(Remaining));
        OnPropertyChanged(nameof(RemainingText));
        OnPropertyChanged(nameof(ChangeText));
    }

    private string? Validate()
    {
        if (Balance <= 0)
        {
            return "Nothing is left to pay.";
        }

        if (IsSplit)
        {
            if (Lines.Count == 0)
            {
                return "Add a payment method for each part of the bill.";
            }

            if (Lines.Any(l => l.Amount <= 0))
            {
                return "Enter an amount for every line.";
            }

            if (Lines.FirstOrDefault(l => l.NeedsReference && string.IsNullOrWhiteSpace(l.Reference)) is { } missing)
            {
                return $"Enter the {missing.Method.Name} reference.";
            }

            return Remaining switch
            {
                > 0 => $"{RemainingText} is still unpaid.",
                < 0 => $"The lines are {Money.Format(-Remaining)} more than the balance.",
                _ => null,
            };
        }

        if (Method is null)
        {
            return "This payment method is not set up. Ask an administrator.";
        }

        if (!Money.TryParse(AmountText, out var amount) || amount <= 0)
        {
            return "Enter the amount.";
        }

        if (IsCash)
        {
            if (TenderedText.Length > 0 && (!Money.TryParse(TenderedText, out var tendered) || tendered < amount))
            {
                return "The cash received is less than the amount.";
            }

            return null;
        }

        if (amount > Balance)
        {
            return $"{Method.Name} cannot be more than the balance of {BalanceText}.";
        }

        return NeedsReference && string.IsNullOrWhiteSpace(Reference) ? $"Enter the {Method.Name} reference." : null;
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
