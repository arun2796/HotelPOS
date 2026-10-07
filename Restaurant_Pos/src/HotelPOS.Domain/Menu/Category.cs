using HotelPOS.Domain.Common;

namespace HotelPOS.Domain.Menu;

/// <summary>A menu section shown as a tab when ordering (Starters, Biryani, Beverages...).</summary>
public sealed class Category : BaseEntity
{
    private Category()
    {
    }

    public Category(string name, int sortOrder)
    {
        Update(name, sortOrder);
        IsActive = true;
    }

    public string Name { get; private set; } = string.Empty;
    public int SortOrder { get; private set; }

    /// <summary>Reserved for category pictures; not used by the v1 screens.</summary>
    public string? ImagePath { get; private set; }

    public bool IsActive { get; private set; }

    public void Update(string name, int sortOrder)
    {
        Name = MenuGuard.Name(name, "Category name");
        SortOrder = sortOrder;
    }

    public void Activate() => IsActive = true;

    /// <summary>The caller checks first that the category has no active items, or deactivates them.</summary>
    public void Deactivate() => IsActive = false;
}
