using HotelPOS.Contracts.Enums;

namespace HotelPOS.Contracts.Menu;

// A waste (prepared food thrown away) or a manual correction. Waste: Quantity is how many portions were lost.
// Adjustment: Quantity is the signed change (negative removes). A reason is always required.
public sealed record AdjustStockRequest
{
    public StockMovementType Kind { get; init; } = StockMovementType.Adjustment;
    public int Quantity { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public static class StockLimits
{
    public const int ReasonMaxLength = 200;
    public const int MaxChange = 100000;
}

public sealed record StockMovementDto
{
    public int Id { get; init; }
    public int MenuItemId { get; init; }
    public string ItemName { get; init; } = string.Empty;
    public StockMovementType Type { get; init; }
    public int PreviousQuantity { get; init; }
    public int QuantityChanged { get; init; }
    public int NewQuantity { get; init; }
    public int? OrderId { get; init; }
    public int? OrderNumber { get; init; }
    public string? UserName { get; init; }
    public DateTime AtUtc { get; init; }
    public string? Remarks { get; init; }
}

// The ledger of one item (newest first) with today's totals: what was sold, refilled and wasted.
public sealed record StockHistoryDto
{
    public int MenuItemId { get; init; }
    public string ItemName { get; init; } = string.Empty;
    public int? Remaining { get; init; }
    public int Sold { get; init; }
    public int Refilled { get; init; }
    public int Wasted { get; init; }
    public IReadOnlyList<StockMovementDto> Movements { get; init; } = Array.Empty<StockMovementDto>();
}
