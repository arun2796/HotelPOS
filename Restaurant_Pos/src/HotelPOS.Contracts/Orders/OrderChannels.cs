using HotelPOS.Contracts.Enums;

namespace HotelPOS.Contracts.Orders;

/// <summary>
/// Helpers for tableless order channels (Phase 22 Step 0.2).
/// Generates synthetic table labels for tickets, bills, notifications, and UI displays
/// when an order is Takeaway or Delivery (e.g. "TA-1016", "DL-1016").
/// </summary>
public static class OrderChannels
{
    public static string TableLabel(OrderType type, string orderNumber, string? tableCode) =>
        type switch
        {
            OrderType.Takeaway => $"TA-{orderNumber}",
            OrderType.Delivery => $"DL-{orderNumber}",
            _ => tableCode ?? string.Empty,
        };

    public static string TableLabel(OrderType type, int orderNumber, string? tableCode) =>
        TableLabel(type, orderNumber.ToString(), tableCode);
}

