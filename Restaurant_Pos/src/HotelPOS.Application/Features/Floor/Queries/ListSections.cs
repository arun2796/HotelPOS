using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Floor.Queries;

public sealed record ListSectionsQuery(bool IncludeInactive) : IQuery<IReadOnlyList<SectionDto>>, IRequirePermission
{
    public string Permission => Permissions.TablesView;
}

internal sealed class ListSectionsHandler : IQueryHandler<ListSectionsQuery, IReadOnlyList<SectionDto>>
{
    private readonly IAppDbContext _db;
    private readonly FloorSections _sections;
    private readonly ICurrentUser _currentUser;

    public ListSectionsHandler(IAppDbContext db, FloorSections sections, ICurrentUser currentUser)
    {
        _db = db;
        _sections = sections;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<SectionDto>>> Handle(ListSectionsQuery request, CancellationToken cancellationToken)
    {
        if (request.IncludeInactive && !(_currentUser.IsInRole(Roles.Admin) || _currentUser.IsInRole(Roles.Manager)))
        {
            return AppErrors.Forbidden("Only managers can list inactive sections.");
        }

        return await _sections.Project(_db.Sections.Where(s => request.IncludeInactive || s.IsActive))
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Name)
            .ToListAsync(cancellationToken);
    }
}
