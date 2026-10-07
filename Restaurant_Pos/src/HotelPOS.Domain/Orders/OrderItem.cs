using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Orders;
using HotelPOS.Domain.Common;

namespace HotelPOS.Domain.Orders;

// Name, price, tax rate, station and modifier prices are copied from the menu when the item is added,
// so later menu changes never alter an order or its bill.
public sealed class OrderItem : BaseEntity
{
    private readonly List<OrderItemModifier> _modifiers = new();

    private OrderItem()
    {
    }

    public OrderItem(
        int menuItemId,
        string itemName,
        decimal unitPrice,
        int quantity,
        string? notes,
        int? taxId,
        decimal taxRatePercent,
        int preparationStationId,
        IEnumerable<OrderItemModifier> modifiers)
    {
        if (quantity is < 1 or > OrderLimits.MaxQuantity)
        {
            throw new DomainException($"Quantity must be between 1 and {OrderLimits.MaxQuantity}.");
        }

        MenuItemId = menuItemId;
        ItemName = itemName;
        UnitPrice = unitPrice;
        Quantity = quantity;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        TaxId = taxId;
        TaxRatePercent = taxRatePercent;
        PreparationStationId = preparationStationId;
        _modifiers.AddRange(modifiers);
        Status = OrderItemStatus.Draft;
    }

    public int OrderId { get; private set; }
    public int BatchNumber { get; private set; }
    public int MenuItemId { get; private set; }
    public string ItemName { get; private set; } = string.Empty;
    public decimal UnitPrice { get; private set; }
    public int Quantity { get; private set; }
    public string? Notes { get; private set; }
    public int? TaxId { get; private set; }
    public decimal TaxRatePercent { get; private set; }
    public int PreparationStationId { get; private set; }
    public OrderItemStatus Status { get; private set; }
    public int? CancelledBy { get; private set; }
    public string? CancelReason { get; private set; }

    public IReadOnlyCollection<OrderItemModifier> Modifiers => _modifiers;

    public decimal LineTotal => (UnitPrice + _modifiers.Sum(m => m.PriceDelta)) * Quantity;

    internal void AssignToBatch(int batchNumber, OrderItemStatus status)
    {
        BatchNumber = batchNumber;
        Status = status;
    }

    internal void MarkSent() => Status = OrderItemStatus.Sent;

    public void CancelSent(int userId, string? reason)
    {
        if (Status != OrderItemStatus.Sent)
        {
            throw new DomainException($"{ItemName} is not with the kitchen and cannot be cancelled here.", Contracts.Common.ErrorCodes.InvalidStateTransition);
        }

        Cancel(userId, reason);
    }

    internal void Cancel(int userId, string? reason)
    {
        if (Status == OrderItemStatus.Cancelled)
        {
            return;
        }

        Status = OrderItemStatus.Cancelled;
        CancelledBy = userId;
        CancelReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }
}

public sealed class OrderItemModifier
{
    private OrderItemModifier()
    {
    }

    public OrderItemModifier(int modifierOptionId, string name, decimal priceDelta)
    {
        ModifierOptionId = modifierOptionId;
        Name = name;
        PriceDelta = priceDelta;
    }

    public int Id { get; private set; }
    public int OrderItemId { get; private set; }
    public int ModifierOptionId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public decimal PriceDelta { get; private set; }
}
