using HotelPOS.Domain.Common;

namespace HotelPOS.Domain.Floor;

/// <summary>An area of the floor (Ground Floor, Terrace...). Groups tables on the waiter's map.</summary>
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

    /// <summary>The caller checks first that the section has no active tables.</summary>
    public void Deactivate() => IsActive = false;
}
