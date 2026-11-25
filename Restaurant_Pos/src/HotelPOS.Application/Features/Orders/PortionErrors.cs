using HotelPOS.Application.Common.Results;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Orders;

// The portion a line was priced from can be deleted by an admin between the moment the order is built and the moment it is saved:
// the database then refuses the line (OrderItems.PortionId -> MenuItemPortions). That is "choose another portion", not a crash.
public static class PortionErrors
{
    private const string PortionForeignKey = "FK_OrderItems_MenuItemPortions_PortionId";

    public static bool IsPortionGone(DbUpdateException exception) =>
        exception.InnerException?.Message.Contains(PortionForeignKey, StringComparison.Ordinal) == true;

    public static AppError NoLongerOffered() =>
        AppErrors.Validation("items", "A portion in this order was just removed from the menu. Choose another portion and send again.");
}
