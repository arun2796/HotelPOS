using System.Globalization;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Menu;

namespace HotelPOS.Desktop.Modules.Kitchen;

// One line of an item's stock ledger as the kitchen reads it.
public sealed class StockHistoryRow
{
    public StockHistoryRow(StockMovementDto movement)
    {
        ItemName = movement.ItemName;
        TimeText = movement.AtUtc.ToLocalTime().ToString("dd MMM HH:mm", CultureInfo.CurrentCulture);
        TypeText = movement.Type switch
        {
            StockMovementType.Opening => "Opening stock",
            StockMovementType.Refill => "Refill",
            StockMovementType.OrderReservation => "Order",
            StockMovementType.OrderRelease => "Order cancelled",
            StockMovementType.Waste => "Waste",
            _ => "Adjustment",
        };
        ChangeText = (movement.QuantityChanged > 0 ? "+" : string.Empty) + movement.QuantityChanged.ToString(CultureInfo.CurrentCulture);
        IsIncrease = movement.QuantityChanged > 0;
        BalanceText = $"{movement.PreviousQuantity} → {movement.NewQuantity}";
        DetailText = string.Join(" · ", new[]
        {
            movement.OrderNumber is { } number ? $"Order #{number}" : null,
            movement.Remarks,
            movement.UserName,
        }.Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    public string ItemName { get; }

    public string TimeText { get; }

    public string TypeText { get; }

    public string ChangeText { get; }

    public bool IsIncrease { get; }

    public string BalanceText { get; }

    public string DetailText { get; }
}
