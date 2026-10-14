using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Security;

// The permissions a set of roles holds, as stored in RolePermissions (editable by the admin).
internal sealed class RolePermissions
{
    private readonly IAppDbContext _db;

    public RolePermissions(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<string>> ForRolesAsync(IEnumerable<string> roles, CancellationToken cancellationToken)
    {
        var roleList = roles.ToList();
        if (roleList.Contains(Roles.Admin, StringComparer.OrdinalIgnoreCase))
        {
            return Permissions.All;
        }

        // No fallback to the default map: a role an admin emptied must stay empty.
        return await _db.RolePermissions
            .Where(rp => roleList.Contains(rp.Role!.Name))
            .Select(rp => rp.Permission)
            .Distinct()
            .OrderBy(p => p)
            .ToListAsync(cancellationToken);
    }
}
