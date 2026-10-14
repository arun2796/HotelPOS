namespace HotelPOS.Domain.Identity;

public sealed class RolePermission
{
    private RolePermission()
    {
    }

    public RolePermission(int roleId, string permission)
    {
        RoleId = roleId;
        Permission = permission;
    }

    public int RoleId { get; private set; }
    public string Permission { get; private set; } = string.Empty;

    public Role? Role { get; private set; }
}

