namespace HotelPOS.Contracts.Enums;

// What happens to food that was already sent to the kitchen when its bill is voided or refunded: it goes back on the
// kitchen's count (Restock) or is written off (Waste).
public enum StockDisposition
{
    Restock = 1,
    Waste = 2,
}
