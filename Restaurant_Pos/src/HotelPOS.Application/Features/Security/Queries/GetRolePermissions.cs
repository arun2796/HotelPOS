using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Security.Queries;

public sealed record GetRolePermissionsQuery(int RoleId) : IQuery<RolePermissionsDto>, IRequirePermission
{
    public string Permission => Permissions.RolesManage;
}

internal sealed class GetRolePermissionsHandler : IQueryHandler<GetRolePermissionsQuery, RolePermissionsDto>
{
    private readonly IAppDbContext _db;

    public GetRolePermissionsHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<Result<RolePermissionsDto>> Handle(GetRolePermissionsQuery request, CancellationToken cancellationToken)
    {
        var role = await _db.Roles
            .Include(r => r.Permissions)
            .FirstOrDefaultAsync(r => r.Id == request.RoleId, cancellationToken);

        if (role is null)
        {
            return AppErrors.NotFound("Role", request.RoleId);
        }

        return RoleMapping.ToPermissionsDto(role);
    }
}
