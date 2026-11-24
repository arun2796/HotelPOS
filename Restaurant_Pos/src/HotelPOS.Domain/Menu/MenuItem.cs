using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Gst;
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

    // HSN/SAC code when it differs from the restaurant default (setting DefaultSacCode); null uses the default.
    public string? HsnSac { get; private set; }

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

    // Kitchen stock quota. StockRemaining is null while the item is not tracked.
    public StockTrackingMode StockMode { get; private set; }
    public int? StockOpening { get; private set; }
    public int? StockRemaining { get; private set; }

    // Added to the unit price when the guest takes the item as a parcel.
    public decimal PackagingCharge { get; private set; }

    public bool IsStockTracked => StockMode != StockTrackingMode.None && StockRemaining is not null;

    // The count at or below which the item shows as Low stock; null uses the default.
    public int? ReorderLevel { get; private set; }

    public void SetReorderLevel(int? level)
    {
        if (level is < 0 or > 100000)
        {
            throw new DomainException("The low-stock level must be between 0 and 100000.");
        }

        ReorderLevel = level is null or 0 ? null : level;
    }

    public void SetPackagingCharge(decimal charge) =>
        PackagingCharge = MenuGuard.Money(charge, 0m, MenuLimits.MaxPrice, "Packaging charge");

    // The supervisor loads a new batch: the opening count and what is left both restart, and the item is
    // available again as soon as there is something to sell.
    public void ConfigureStock(StockTrackingMode mode, int? quantity)
    {
        if (mode == StockTrackingMode.None)
        {
            StockMode = StockTrackingMode.None;
            StockOpening = null;
            StockRemaining = null;
            return;
        }

        if (quantity is null or < 0 or > 100000)
        {
            throw new DomainException("Enter a stock count between 0 and 100000.");
        }

        StockMode = mode;
        StockOpening = quantity;
        StockRemaining = quantity;
        IsAvailable = quantity > 0;
    }

    // Adds to what is left (a second vessel is loaded) without restarting the opening count.
    public void AddStock(int quantity)
    {
        if (!IsStockTracked)
        {
            throw new DomainException($"{Name} has no stock count. Set one first.");
        }

        if (quantity < 1 || quantity > 100000)
        {
            throw new DomainException("Enter a quantity between 1 and 100000.");
        }

        StockRemaining += quantity;
        StockOpening = Math.Max(StockOpening ?? 0, StockRemaining ?? 0);
        IsAvailable = true;
    }

    // Returns false when an Exact item cannot cover the quantity. An Approximate item never refuses: its count is a
    // guess, so it simply sells out when the guess runs to zero.
    public bool TryConsumeStock(int quantity)
    {
        if (!IsStockTracked)
        {
            return true;
        }

        var left = StockRemaining ?? 0;
        if (StockMode == StockTrackingMode.Exact && quantity > left)
        {
            return false;
        }

        StockRemaining = Math.Max(0, left - quantity);
        if (StockRemaining == 0)
        {
            IsAvailable = false;
        }

        return true;
    }

    // A sent item was cancelled: the portions go back, and an item that sold out because of the count is on again.
    public void RestoreStock(int quantity)
    {
        if (!IsStockTracked)
        {
            return;
        }

        var wasOut = (StockRemaining ?? 0) == 0;
        StockRemaining = (StockRemaining ?? 0) + quantity;
        if (wasOut && StockRemaining > 0)
        {
            IsAvailable = true;
        }
    }

    public void SetImage(string? imagePath) => ImagePath = imagePath;

    public void SetHsnSac(string? hsnSac)
    {
        var clean = string.IsNullOrWhiteSpace(hsnSac) ? null : hsnSac.Trim();
        if (clean is not null && (clean.Length is < 4 or > GstLimits.HsnMaxLength || !clean.All(char.IsAsciiDigit)))
        {
            throw new DomainException("An HSN/SAC code is 4 to 8 digits.", ErrorCodes.ValidationError);
        }

        HsnSac = clean;
    }

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
