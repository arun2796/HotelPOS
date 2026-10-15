using HotelPOS.Contracts.Enums;

namespace HotelPOS.Domain.Cash;

public sealed record CashBreakdown(
    decimal FloatAmount,
    decimal CashSales,
    decimal CashRefunds,
    decimal PayIns,
    decimal PayOuts,
    decimal TillVouchers)
{
    public decimal Expected => FloatAmount + CashSales - CashRefunds + PayIns - PayOuts - TillVouchers;
}

// Expected cash = float + cash payments − cash refunds + pay-ins − pay-outs − till vouchers, for one shift only.
public static class ExpectedCash
{
    // Payment amounts as stored: the amount applied to the bill (change already handed back), refunds negative.
    public static CashBreakdown Of(decimal floatAmount, IEnumerable<decimal> cashPaymentAmounts, IEnumerable<CashMovement> movements)
    {
        var payments = cashPaymentAmounts.ToList();
        var moves = movements.ToList();
        return new CashBreakdown(
            floatAmount,
            payments.Where(a => a > 0).Sum(),
            -payments.Where(a => a < 0).Sum(),
            Sum(moves, CashMovementType.PayIn),
            Sum(moves, CashMovementType.PayOut),
            Sum(moves, CashMovementType.TillVoucher));
    }

    private static decimal Sum(IEnumerable<CashMovement> movements, CashMovementType type) =>
        movements.Where(m => m.Type == type).Sum(m => m.Amount);
}
