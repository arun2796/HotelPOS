using HotelPOS.Contracts.Cash;
using HotelPOS.Contracts.Common;
using HotelPOS.Domain.Common;

namespace HotelPOS.Domain.Cash;

// How many of one note or coin were counted, at the value the note had when it was counted.
public readonly record struct DenominationCount
{
    public DenominationCount(int denominationId, decimal value, int quantity)
    {
        if (quantity is < 0 or > CashLimits.MaxQuantity)
        {
            throw new DomainException($"Enter a quantity from 0 to {CashLimits.MaxQuantity}.", ErrorCodes.ValidationError);
        }

        if (value <= 0)
        {
            throw new DomainException("A denomination's value must be above zero.", ErrorCodes.ValidationError);
        }

        DenominationId = denominationId;
        Value = value;
        Quantity = quantity;
    }

    public int DenominationId { get; }
    public decimal Value { get; }
    public int Quantity { get; }

    public decimal Amount => Value * Quantity;

    public static decimal Total(IEnumerable<DenominationCount> counts) => counts.Sum(c => c.Amount);
}
