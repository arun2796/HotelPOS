using HotelPOS.Contracts.Enums;

namespace HotelPOS.Contracts.Menu;

public enum StockStatus
{
    NotTracked = 0,
    Available = 1,
    Low = 2,
    Out = 3,
    Unavailable = 4,
}

// One rule for the stock status shown on every screen: the server, the desktop and the web all use it.
public static class StockStatusRules
{
    // Used when no reorder level was set for the item.
    public const int DefaultLowLevel = 5;

    public static StockStatus For(StockTrackingMode mode, int? remaining, bool isAvailable, int? reorderLevel)
    {
        if (mode == StockTrackingMode.None || remaining is null)
        {
            return isAvailable ? StockStatus.NotTracked : StockStatus.Unavailable;
        }

        if (remaining <= 0)
        {
            return StockStatus.Out;
        }

        if (!isAvailable)
        {
            return StockStatus.Unavailable;
        }

        return remaining <= (reorderLevel ?? DefaultLowLevel) ? StockStatus.Low : StockStatus.Available;
    }

    public static string Text(StockStatus status) => status switch
    {
        StockStatus.Available => "Available",
        StockStatus.Low => "Low stock",
        StockStatus.Out => "Out of stock",
        StockStatus.Unavailable => "Temporarily unavailable",
        _ => "Not counted",
    };
}

public sealed record SetReorderLevelRequest
{
    // The count at or below which the item shows as Low stock. Empty or 0 goes back to the default.
    public int? ReorderLevel { get; init; }
}

// One row of the kitchen stock dashboard (Manager, Admin, Kitchen).
public sealed record StockOverviewRowDto
{
    public int MenuItemId { get; init; }
    public string ItemName { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Station { get; init; } = string.Empty;
    public StockTrackingMode Mode { get; init; }
    public int Opening { get; init; }
    public int Reserved { get; init; }
    public int Sold { get; init; }
    public int Refilled { get; init; }
    public int Wasted { get; init; }
    public int Available { get; init; }
    public int? ReorderLevel { get; init; }
    public StockStatus Status { get; init; }
    public DateTime? LastUpdatedUtc { get; init; }
    public string? UpdatedBy { get; init; }
}

public sealed record StockOverviewDto
{
    public DateTime ServerTimeUtc { get; init; }
    public IReadOnlyList<StockOverviewRowDto> Items { get; init; } = Array.Empty<StockOverviewRowDto>();
}
