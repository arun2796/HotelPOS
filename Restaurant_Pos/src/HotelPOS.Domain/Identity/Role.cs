using HotelPOS.Domain.Common;

namespace HotelPOS.Domain.Identity;

public sealed class Role : BaseEntity
{
    private Role()
    {
    }

    public Role(string name, string? description, bool isSystem)
    {
        Name = name;
        Description = description;
        IsSystem = isSystem;
    }

    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }

    public bool IsSystem { get; private set; }
}
