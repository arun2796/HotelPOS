using HotelPOS.Contracts.Enums;

namespace HotelPOS.Desktop.Modules.Common;

public static class StatusLabels
{
    public static string For(OrderStatus status) => status switch
    {
        OrderStatus.BillRequested => "Bill requested",
        _ => status.ToString(),
    };
}
