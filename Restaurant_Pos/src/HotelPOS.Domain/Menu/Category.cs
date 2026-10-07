using HotelPOS.Domain.Common;

namespace HotelPOS.Domain.Menu;

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

    public string? ImagePath { get; private set; }

    public bool IsActive { get; private set; }

    public void Update(string name, int sortOrder)
    {
        Name = MenuGuard.Name(name, "Category name");
        SortOrder = sortOrder;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
