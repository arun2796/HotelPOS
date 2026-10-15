using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Menu.Queries;

public sealed record ListModifierGroupsQuery(bool IncludeInactive) : IQuery<IReadOnlyList<ModifierGroupDto>>, IRequirePermission
{
    public string Permission => Permissions.MenuView;
}

internal sealed class ListModifierGroupsHandler : IQueryHandler<ListModifierGroupsQuery, IReadOnlyList<ModifierGroupDto>>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public ListModifierGroupsHandler(IAppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<ModifierGroupDto>>> Handle(ListModifierGroupsQuery request, CancellationToken cancellationToken)
    {
        var includeInactive = request.IncludeInactive;
        if (includeInactive && !(_currentUser.IsInRole(Roles.Admin) || _currentUser.IsInRole(Roles.Manager)))
        {
            return AppErrors.Forbidden("Only managers can list inactive modifier groups.");
        }

        var groups = await _db.ModifierGroups.AsNoTracking()
            .Include(g => g.Options)
            .Where(g => includeInactive || g.IsActive)
            .OrderBy(g => g.Name)
            .ToListAsync(cancellationToken);
        return groups.Select(g => MenuMapping.ToDto(g, includeInactive)).ToList();
    }
}
