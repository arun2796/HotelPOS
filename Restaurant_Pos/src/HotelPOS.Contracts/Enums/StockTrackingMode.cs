namespace HotelPOS.Contracts.Enums;

// How the kitchen counts an item down.
// Exact: a fixed batch count (50 idlis per vessel); an order for more than what is left is refused.
// Approximate: an estimated portion quota (biryani, batter); the count only guides, the last orders may overshoot it.
public enum StockTrackingMode
{
    None = 0,
    Exact = 1,
    Approximate = 2,
}
