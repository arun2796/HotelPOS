using HotelPOS.Contracts.Menu;
using HotelPOS.Domain.Common;

namespace HotelPOS.Domain.Menu;

// What an admin enters for one portion of an item. Id is the existing row being kept (null for a new portion).
public sealed record PortionSpec(int? Id, string Name, decimal? Quantity, string? Unit, decimal Price, bool IsDefault, bool IsActive, int SortOrder);

// One way an item is sold: Quarter, Half, Full, 1 KG... with its own price. MenuItem.Price always mirrors the default one.
public sealed class MenuItemPortion : BaseEntity
{
    private MenuItemPortion()
    {
    }

    internal MenuItemPortion(PortionSpec spec)
    {
        Apply(spec);
    }

    public int MenuItemId { get; private set; }
    public MenuItem? MenuItem { get; private set; }
    public string Name { get; private set; } = string.Empty;

    // Label only ("500" "GM"): it is shown to staff and frozen on order lines, it never changes a price.
    public decimal? Quantity { get; private set; }
    public string? Unit { get; private set; }
    public decimal Price { get; private set; }
    public bool IsDefault { get; private set; }
    public bool IsActive { get; private set; }
    public int SortOrder { get; private set; }

    internal void Apply(PortionSpec spec)
    {
        var name = MenuGuard.Name(spec.Name, "Portion name");
        if (name.Length > MenuLimits.PortionNameMaxLength)
        {
            throw new DomainException($"A portion name can have at most {MenuLimits.PortionNameMaxLength} characters.");
        }

        if (spec.Quantity is { } quantity && (quantity <= 0 || quantity > MenuLimits.PortionQuantityMax))
        {
            throw new DomainException($"Portion quantity for {name} must be above 0 and at most {MenuLimits.PortionQuantityMax:0}.");
        }

        string? unit = null;
        if (!string.IsNullOrWhiteSpace(spec.Unit))
        {
            unit = PortionUnits.Normalize(spec.Unit)
                ?? throw new DomainException($"Choose a unit for {name} from the list: {string.Join(", ", PortionUnits.All)}.");
            if (spec.Quantity is null)
            {
                throw new DomainException($"Enter a quantity for {name}, or clear its unit.");
            }
        }

        Name = name;
        Quantity = spec.Quantity;
        Unit = unit;
        Price = MenuGuard.Money(spec.Price, 0m, MenuLimits.MaxPrice, $"Price of {name}");
        IsDefault = spec.IsDefault;
        IsActive = spec.IsActive;
        SortOrder = spec.SortOrder;
    }

    internal void SetPrice(decimal price) => Price = MenuGuard.Money(price, 0m, MenuLimits.MaxPrice, $"Price of {Name}");
}
