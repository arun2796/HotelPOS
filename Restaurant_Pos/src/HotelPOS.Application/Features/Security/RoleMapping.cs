using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Identity;

namespace HotelPOS.Application.Features.Security;

internal static class RoleMapping
{
    // The Admin role always shows every permission, whatever rows it has.
    public static RolePermissionsDto ToPermissionsDto(Role role)
    {
        var permissions = string.Equals(role.Name, Roles.Admin, StringComparison.OrdinalIgnoreCase)
            ? Permissions.All
            : role.Permissions.Select(p => p.Permission).OrderBy(p => p).ToList();

        return new RolePermissionsDto
        {
            RoleId = role.Id,
            RoleName = role.Name,
            Description = role.Description,
            IsSystem = role.IsSystem,
            Permissions = permissions,
        };
    }
}
