using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Cash;
using HotelPOS.Contracts.Enums;
using HotelPOS.Desktop.Modules.Billing;

namespace HotelPOS.Desktop.Modules.Cash;

public sealed partial class DenominationRowViewModel : ObservableObject
{
    public DenominationRowViewModel(DenominationDto denomination, Action changed)
    {
        Denomination = denomination;
        _changed = changed;
    }

    private readonly Action _changed;

    public DenominationDto Denomination { get; }

    public string Label => Denomination.Label;

    public bool IsCoin => Denomination.Kind == DenominationKind.Coin;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QuantityText), nameof(AmountText), nameof(Amount))]
    private int _quantity;

    // Typed on the keyboard or the touch keypad; anything that is not a whole number counts as zero.
    public string QuantityText
    {
        get => Quantity.ToString(CultureInfo.CurrentCulture);
        set => Quantity = int.TryParse(value, NumberStyles.Integer, CultureInfo.CurrentCulture, out var q) ? Math.Clamp(q, 0, CashLimits.MaxQuantity) : 0;
    }

    // Display only: the server computes the count again from the quantities.
    public decimal Amount => Denomination.Value * Quantity;

    public string AmountText => Money.Format(Amount);

    partial void OnQuantityChanged(int value) => _changed();

    [RelayCommand]
    private void Increment() => Quantity = Math.Min(CashLimits.MaxQuantity, Quantity + 1);

    [RelayCommand]
    private void Decrement() => Quantity = Math.Max(0, Quantity - 1);
}

// The counting sheet of open, close, handover and force close: one row per active note and coin, a live total and,
// unless a blind count hides it, the expected cash and the difference.
public sealed partial class DenominationSheetViewModel : ObservableObject
{
    public DenominationSheetViewModel(IEnumerable<DenominationDto> denominations, decimal? expected = null, bool blind = false)
    {
        Expected = expected;
        IsBlind = blind;
        foreach (var denomination in denominations.Where(d => d.IsActive).OrderBy(d => d.SortOrder).ThenByDescending(d => d.Value))
        {
            var row = new DenominationRowViewModel(denomination, Recalculate);
            (row.IsCoin ? Coins : Notes).Add(row);
        }
    }

    public ObservableCollection<DenominationRowViewModel> Notes { get; } = new();

    public ObservableCollection<DenominationRowViewModel> Coins { get; } = new();

    public IEnumerable<DenominationRowViewModel> Rows => Notes.Concat(Coins);

    public decimal? Expected { get; }

    public bool IsBlind { get; }

    public bool ShowsExpected => Expected is not null && !IsBlind;

    public decimal Total => Rows.Sum(r => r.Amount);

    public string TotalText => Money.Format(Total);

    public string ExpectedText => Expected is { } e ? Money.Format(e) : "—";

    public decimal? Variance => ShowsExpected ? Total - Expected : null;

    public string VarianceText => Variance switch
    {
        null => string.Empty,
        0m => "Matches the expected cash",
        < 0m => $"Short by {Money.Format(-Variance.Value)}",
        _ => $"Over by {Money.Format(Variance.Value)}",
    };

    public bool HasVariance => Variance is { } v && v != 0m;

    public IReadOnlyList<DenominationCountDto> ToCounts() =>
        Rows.Where(r => r.Quantity > 0).Select(r => new DenominationCountDto { DenominationId = r.Denomination.Id, Quantity = r.Quantity }).ToList();

    public void Fill(IEnumerable<CountedDenominationDto> counts)
    {
        foreach (var count in counts)
        {
            if (Rows.FirstOrDefault(r => r.Denomination.Id == count.DenominationId) is { } row)
            {
                row.Quantity = count.Quantity;
            }
        }
    }

    [RelayCommand]
    private void Clear()
    {
        foreach (var row in Rows)
        {
            row.Quantity = 0;
        }
    }

    private void Recalculate()
    {
        OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(TotalText));
        OnPropertyChanged(nameof(Variance));
        OnPropertyChanged(nameof(VarianceText));
        OnPropertyChanged(nameof(HasVariance));
    }
}
