using HotelPOS.Domain.Common;

namespace HotelPOS.Domain.Floor;

public sealed class Section : BaseEntity
{
    private Section()
    {
    }

    public Section(string name, int sortOrder)
    {
        Update(name, sortOrder);
        IsActive = true;
    }

    public string Name { get; private set; } = string.Empty;
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }

    public void Update(string name, int sortOrder)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Section name is required.");
        }

        Name = name.Trim();
        SortOrder = sortOrder;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
