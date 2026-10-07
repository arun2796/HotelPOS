using HotelPOS.Contracts.Menu;
using HotelPOS.Domain.Common;

namespace HotelPOS.Domain.Menu;

public sealed class ModifierGroup : BaseEntity
{
    private readonly List<ModifierOption> _options = new();

    private ModifierGroup()
    {
    }

    public ModifierGroup(string name, int minSelections, int maxSelections)
    {
        Update(name, minSelections, maxSelections);
        IsActive = true;
    }

    public string Name { get; private set; } = string.Empty;
    public int MinSelections { get; private set; }
    public int MaxSelections { get; private set; }
    public bool IsActive { get; private set; }

    public IReadOnlyCollection<ModifierOption> Options => _options;

    public void Update(string name, int minSelections, int maxSelections)
    {
        if (minSelections < 0 || maxSelections < 1 || minSelections > maxSelections || maxSelections > MenuLimits.MaxModifierSelections)
        {
            throw new DomainException(
                $"Selections must satisfy 0 <= minimum <= maximum, with a maximum from 1 to {MenuLimits.MaxModifierSelections}.");
        }

        Name = MenuGuard.Name(name, "Modifier group name");
        MinSelections = minSelections;
        MaxSelections = maxSelections;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}

public sealed class ModifierOption : BaseEntity
{
    private ModifierOption()
    {
    }

    public ModifierOption(int modifierGroupId, string name, decimal priceDelta, int sortOrder)
    {
        ModifierGroupId = modifierGroupId;
        Update(name, priceDelta, sortOrder);
        IsActive = true;
    }

    public int ModifierGroupId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public decimal PriceDelta { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }

    public void Update(string name, decimal priceDelta, int sortOrder)
    {
        Name = MenuGuard.Name(name, "Option name");
        PriceDelta = MenuGuard.Money(priceDelta, -MenuLimits.MaxPriceDelta, MenuLimits.MaxPriceDelta, "Price change");
        SortOrder = sortOrder;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
