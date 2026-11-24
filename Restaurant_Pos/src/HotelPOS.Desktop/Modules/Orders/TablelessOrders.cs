using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Orders;
using HotelPOS.Desktop.Modules.Common;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Navigation;

namespace HotelPOS.Desktop.Modules.Orders;

// A takeaway or delivery order has no table to open: its next step is the bill. The counter staff land on the Billing
// screen with the bill ready (the bill is generated first when it has not been yet); anyone else is told where it is.
internal static class TablelessOrders
{
    // Returns the message to show, or null when the person was taken to the bill.
    public static async Task<string?> OpenAsync(
        OrderSummaryDto order,
        INavigationService navigation,
        IOrdersApi orders,
        IBillingApi? billing,
        bool canTakePayment)
    {
        var hasBill = order.Status is OrderStatus.BillRequested or OrderStatus.Billed;
        if (!canTakePayment || billing is null)
        {
            return hasBill
                ? $"Order #{order.OrderNumber}: its bill is waiting at the billing counter."
                : $"Order #{order.OrderNumber} is with the kitchen. The counter takes payment when its bill is requested.";
        }

        int? billId;
        if (hasBill)
        {
            var pending = await billing.GetPendingAsync();
            billId = pending.Data?.FirstOrDefault(b => b.OrderId == order.Id)?.Id;
        }
        else
        {
            var requested = await orders.RequestBillAsync(order.Id, Guid.NewGuid());
            if (!requested.Success)
            {
                return ApiFailures.Describe(requested);
            }

            billId = requested.Data?.Id;
        }

        await navigation.NavigateToAsync(ModuleRegistry.Billing, billId);
        return null;
    }
}
