namespace HotelPOS.Contracts.Enums;

// Why a kitchen stock count changed. Every change is one row of the stock ledger, so a count can always be explained.
public enum StockMovementType
{
    Opening = 1,
    Refill = 2,
    OrderReservation = 3,
    OrderRelease = 4,
    Waste = 5,
    Adjustment = 6,
}
