using HotelPOS.Contracts.Cash;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Domain.Common;

namespace HotelPOS.Domain.Cash;

// A note or coin counted in the drawer. Never deleted once used: a withdrawn note is deactivated. Counts keep the
// value they were counted at, so changing a value later does not rewrite past counts.
public sealed class Denomination : BaseEntity
{
    private Denomination()
    {
    }

    public Denomination(decimal value, string label, DenominationKind kind, int sortOrder)
    {
        Update(value, label, kind, sortOrder, isActive: true);
    }

    public decimal Value { get; private set; }
    public string Label { get; private set; } = string.Empty;
    public DenominationKind Kind { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }

    public void Update(decimal value, string label, DenominationKind kind, int sortOrder, bool isActive)
    {
        if (value <= 0 || value > CashLimits.MaxDenominationValue || decimal.Round(value, 2) != value)
        {
            throw new DomainException("A denomination's value must be above zero with at most two decimals.", ErrorCodes.ValidationError);
        }

        var trimmed = label?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > CashLimits.LabelMaxLength)
        {
            throw new DomainException($"A denomination's label must be 1 to {CashLimits.LabelMaxLength} characters.", ErrorCodes.ValidationError);
        }

        if (!Enum.IsDefined(kind))
        {
            throw new DomainException("A denomination is a note or a coin.", ErrorCodes.ValidationError);
        }

        Value = value;
        Label = trimmed;
        Kind = kind;
        SortOrder = sortOrder;
        IsActive = isActive;
    }
}
