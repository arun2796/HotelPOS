using HotelPOS.Contracts.Cash;
using HotelPOS.Contracts.Common;
using HotelPOS.Domain.Common;

namespace HotelPOS.Domain.Cash;

// Money for the day taken outside any till (the owner's UPI, a cheque), so the day's totals reconcile.
public sealed class NonTillCollection : BaseEntity
{
    private NonTillCollection()
    {
    }

    public NonTillCollection(DateOnly businessDay, string method, decimal amount, string? reference, string? note, int recordedBy, DateTime nowUtc)
    {
        var cleanMethod = method?.Trim() ?? string.Empty;
        if (cleanMethod.Length is 0 or > CashLimits.MethodMaxLength)
        {
            throw new DomainException($"Enter how the money was received (up to {CashLimits.MethodMaxLength} characters).", ErrorCodes.ValidationError);
        }

        if (amount <= 0 || amount > CashLimits.MaxAmount || decimal.Round(amount, 2) != amount)
        {
            throw new DomainException("Enter an amount above zero with at most two decimals.", ErrorCodes.ValidationError);
        }

        BusinessDay = businessDay;
        Method = cleanMethod;
        Amount = amount;
        Reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        RecordedBy = recordedBy;
        RecordedAt = nowUtc;
    }

    public DateOnly BusinessDay { get; private set; }
    public string Method { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public string? Reference { get; private set; }
    public string? Note { get; private set; }
    public int RecordedBy { get; private set; }
    public DateTime RecordedAt { get; private set; }
}
