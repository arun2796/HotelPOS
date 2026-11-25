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
        IEnumerable<OrderItemModifier> modifiers,
        bool isParcel = false,
        decimal packagingCharge = 0m,
        string? seatCode = null)
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
        IsParcel = isParcel;
        PackagingCharge = packagingCharge;
        SeatCode = string.IsNullOrWhiteSpace(seatCode) ? null : seatCode.Trim().ToUpperInvariant();
    }

    // A parcel's packaging charge is already inside UnitPrice (billing needs no extra line); it is kept apart so
    // the screens can show what part of the price is packing.
    public bool IsParcel { get; private set; }
    public decimal PackagingCharge { get; private set; }

    // "4A": the table code plus the seat letter the dish is for. Null means the whole table.
    public string? SeatCode { get; private set; }

    // The portion the guest ordered ("Half"), frozen with the line. Null for an item that has a single price (the line is
    // then exactly what it was before portions existed). PortionId is cleared if the admin deletes the portion; the
    // name, quantity and unit stay.
    public int? PortionId { get; private set; }
    public string? PortionName { get; private set; }
    public decimal? PortionQuantity { get; private set; }
    public string? PortionUnit { get; private set; }

    public void SetPortion(int portionId, string name, decimal? quantity, string? unit)
    {
        PortionId = portionId;
        PortionName = name;
        PortionQuantity = quantity;
        PortionUnit = unit;
    }

    // "Chicken Biryani (Half)", "Chicken Biryani (Half) (Parcel)": what the kitchen, the bill and the reports read.
    public string DisplayName => HotelPOS.Contracts.Menu.ItemDisplayName.Compose(ItemName, PortionName);

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
