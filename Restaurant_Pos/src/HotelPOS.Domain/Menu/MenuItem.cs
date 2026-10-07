using HotelPOS.Contracts.Menu;
using HotelPOS.Domain.Common;

namespace HotelPOS.Domain.Menu;

public sealed class MenuItem : BaseEntity, IHasRowVersion
{
    private readonly List<MenuItemModifierGroup> _modifierGroups = new();

    private MenuItem()
    {
    }

    public MenuItem(int categoryId, string name, string? code, string? description, decimal price, int? taxId, int preparationStationId, int sortOrder)
    {
        Update(categoryId, name, code, description, price, taxId, preparationStationId, sortOrder);
        IsAvailable = true;
        IsActive = true;
    }

    public int CategoryId { get; private set; }
    public Category? Category { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Code { get; private set; }
    public string? Description { get; private set; }
    public decimal Price { get; private set; }
    public int? TaxId { get; private set; }
    public Tax? Tax { get; private set; }
    public int PreparationStationId { get; private set; }
    public PreparationStation? PreparationStation { get; private set; }

    public bool IsAvailable { get; private set; }

    public bool IsActive { get; private set; }

    public string? ImagePath { get; private set; }

    public int SortOrder { get; private set; }
    public uint RowVersion { get; set; }

    public IReadOnlyCollection<MenuItemModifierGroup> ModifierGroups => _modifierGroups;

    public static string? NormalizeCode(string? code) => string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToUpperInvariant();

    public void Update(int categoryId, string name, string? code, string? description, decimal price, int? taxId, int preparationStationId, int sortOrder)
    {
        if (categoryId <= 0 || preparationStationId <= 0)
        {
            throw new DomainException("An item needs a category and a preparation station.");
        }

        var trimmedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (trimmedDescription is { Length: > MenuLimits.DescriptionMaxLength })
        {
            throw new DomainException($"Description can have at most {MenuLimits.DescriptionMaxLength} characters.");
        }

        CategoryId = categoryId;
        Name = MenuGuard.Name(name, "Item name");
        Code = MenuGuard.Code(code, "Item code");
        Description = trimmedDescription;
        Price = MenuGuard.Money(price, 0m, MenuLimits.MaxPrice, "Price");
        TaxId = taxId;
        PreparationStationId = preparationStationId;
        SortOrder = sortOrder;
    }

    public void SetAvailability(bool isAvailable) => IsAvailable = isAvailable;

    public void SetImage(string? imagePath) => ImagePath = imagePath;

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    public void SetModifierGroups(IReadOnlyList<int> modifierGroupIds)
    {
        if (modifierGroupIds.Distinct().Count() != modifierGroupIds.Count)
        {
            throw new DomainException("A modifier group can be linked only once.");
        }

        _modifierGroups.RemoveAll(link => !modifierGroupIds.Contains(link.ModifierGroupId));
        for (var i = 0; i < modifierGroupIds.Count; i++)
        {
            var link = _modifierGroups.FirstOrDefault(l => l.ModifierGroupId == modifierGroupIds[i]);
            if (link is null)
            {
                _modifierGroups.Add(new MenuItemModifierGroup { MenuItem = this, ModifierGroupId = modifierGroupIds[i], SortOrder = i + 1 });
            }
            else
            {
                link.SortOrder = i + 1;
            }
        }
    }
}

public sealed class MenuItemModifierGroup
{
    public int MenuItemId { get; set; }
    public MenuItem? MenuItem { get; set; }
    public int ModifierGroupId { get; set; }
    public ModifierGroup? ModifierGroup { get; set; }
    public int SortOrder { get; set; }
}
