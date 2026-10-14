using HotelPOS.Contracts.Cash;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Domain.Common;

namespace HotelPOS.Domain.Cash;

// Cash put into (pay-in) or taken out of (pay-out, till voucher) the drawer outside a bill. Amount is always positive;
// the type gives the direction.
public sealed class CashMovement : BaseEntity
{
    private CashMovement()
    {
    }

    internal CashMovement(CashMovementType type, decimal amount, string reason, int? approvedBy)
    {
        if (!Enum.IsDefined(type))
        {
            throw new DomainException("Choose pay-in, pay-out or till voucher.", ErrorCodes.ValidationError);
        }

        if (amount <= 0 || amount > CashLimits.MaxAmount || decimal.Round(amount, 2) != amount)
        {
            throw new DomainException("Enter an amount above zero with at most two decimals.", ErrorCodes.ValidationError);
        }

        var cleanReason = reason?.Trim();
        if (string.IsNullOrEmpty(cleanReason))
        {
            throw new DomainException("Enter the reason for the cash movement.", ErrorCodes.ValidationError);
        }

        Type = type;
        Amount = amount;
        Reason = cleanReason;
        ApprovedBy = approvedBy;
    }

    public int ShiftId { get; private set; }
    public CashMovementType Type { get; private set; }
    public decimal Amount { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public int? ApprovedBy { get; private set; }

    // Effect on the cash in the drawer.
    public decimal SignedAmount => Type == CashMovementType.PayIn ? Amount : -Amount;
}
