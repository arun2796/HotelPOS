using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Security.Queries;

public sealed record ListRolePermissionsQuery : IQuery<IReadOnlyList<RolePermissionsDto>>, IRequirePermission
{
    public string Permission => Permissions.RolesManage;
}

internal sealed class ListRolePermissionsHandler : IQueryHandler<ListRolePermissionsQuery, IReadOnlyList<RolePermissionsDto>>
{
    private readonly IAppDbContext _db;

    public ListRolePermissionsHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<Result<IReadOnlyList<RolePermissionsDto>>> Handle(ListRolePermissionsQuery request, CancellationToken cancellationToken)
    {
        var roles = await _db.Roles
            .Include(r => r.Permissions)
            .OrderBy(r => r.Name)
            .ToListAsync(cancellationToken);

        return roles.Select(RoleMapping.ToPermissionsDto).ToList();
    }
}
