using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Security;
using HotelPOS.Contracts.Users;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Security.Queries;

public sealed record ListRolesQuery : IQuery<IReadOnlyList<RoleDto>>, IRequirePermission
{
    public string Permission => Permissions.RolesManage;
}

internal sealed class ListRolesHandler : IQueryHandler<ListRolesQuery, IReadOnlyList<RoleDto>>
{
    private readonly IAppDbContext _db;

    public ListRolesHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<Result<IReadOnlyList<RoleDto>>> Handle(ListRolesQuery request, CancellationToken cancellationToken)
    {
        var roles = await _db.Roles.AsNoTracking().OrderBy(r => r.Id).ToListAsync(cancellationToken);
        return roles.Select(r => new RoleDto { Id = r.Id, Name = r.Name, Description = r.Description, IsSystem = r.IsSystem }).ToList();
    }
}
