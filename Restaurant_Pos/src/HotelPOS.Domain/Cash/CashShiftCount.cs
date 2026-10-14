using HotelPOS.Contracts.Enums;

namespace HotelPOS.Domain.Cash;

public sealed class CashShiftCount
{
    private CashShiftCount()
    {
    }

    internal CashShiftCount(CashCountPhase phase, DenominationCount count)
    {
        Phase = phase;
        DenominationId = count.DenominationId;
        Value = count.Value;
        Quantity = count.Quantity;
    }

    public int Id { get; private set; }
    public int ShiftId { get; private set; }
    public CashCountPhase Phase { get; private set; }
    public int DenominationId { get; private set; }
    public decimal Value { get; private set; }
    public int Quantity { get; private set; }

    public decimal Amount => Value * Quantity;
}
