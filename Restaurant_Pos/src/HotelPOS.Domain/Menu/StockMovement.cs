using HotelPOS.Contracts.Enums;
using HotelPOS.Domain.Common;
using HotelPOS.Domain.Orders;

namespace HotelPOS.Domain.Menu;

// One line of the kitchen stock ledger: what the count was, how much it moved, what it became, why, for which order and
// who did it (CreatedBy / CreatedAt are stamped on save). A count is never changed without a row here.
public sealed class StockMovement : BaseEntity
{
    private StockMovement()
    {
    }

    public StockMovement(int menuItemId, StockMovementType type, int previousQuantity, int quantityChanged, int newQuantity, int? orderId, string? remarks)
    {
        MenuItemId = menuItemId;
        Type = type;
        PreviousQuantity = previousQuantity;
        QuantityChanged = quantityChanged;
        NewQuantity = newQuantity;
        OrderId = orderId;
        Remarks = string.IsNullOrWhiteSpace(remarks) ? null : remarks.Trim();
    }

    public int MenuItemId { get; private set; }
    public StockMovementType Type { get; private set; }
    public int PreviousQuantity { get; private set; }
    public int QuantityChanged { get; private set; }
    public int NewQuantity { get; private set; }
    public int? OrderId { get; private set; }
    public Order? Order { get; private set; }
    public string? Remarks { get; private set; }

    // For an order that is created in the same transaction: the order id exists only after it is saved.
    public void LinkOrder(Order order)
    {
        Order = order;
    }
}
