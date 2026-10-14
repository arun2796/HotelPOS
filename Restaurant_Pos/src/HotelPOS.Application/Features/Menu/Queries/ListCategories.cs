using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Menu.Queries;

public sealed record ListCategoriesQuery(bool IncludeInactive) : IQuery<IReadOnlyList<CategoryDto>>, IRequirePermission
{
    public string Permission => Permissions.MenuView;
}

internal sealed class ListCategoriesHandler : IQueryHandler<ListCategoriesQuery, IReadOnlyList<CategoryDto>>
{
    private readonly IAppDbContext _db;
    private readonly MenuCategories _categories;
    private readonly ICurrentUser _currentUser;

    public ListCategoriesHandler(IAppDbContext db, MenuCategories categories, ICurrentUser currentUser)
    {
        _db = db;
        _categories = categories;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<CategoryDto>>> Handle(ListCategoriesQuery request, CancellationToken cancellationToken)
    {
        if (request.IncludeInactive && !(_currentUser.IsInRole(Roles.Admin) || _currentUser.IsInRole(Roles.Manager)))
        {
            return AppErrors.Forbidden("Only managers can list inactive categories.");
        }

        return await _categories.Project(_db.Categories.Where(c => request.IncludeInactive || c.IsActive))
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);
    }
}
