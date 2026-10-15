using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Menu.Queries;

public sealed record ListMenuItemsQuery(MenuItemQuery Input)
    : IQuery<IReadOnlyList<MenuItemDto>>, IHasInput<MenuItemQuery>, IRequirePermission
{
    public string Permission => Permissions.MenuView;
}

internal sealed class ListMenuItemsHandler : IQueryHandler<ListMenuItemsQuery, IReadOnlyList<MenuItemDto>>
{
    private readonly MenuItems _items;
    private readonly ICurrentUser _currentUser;

    public ListMenuItemsHandler(MenuItems items, ICurrentUser currentUser)
    {
        _items = items;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<MenuItemDto>>> Handle(ListMenuItemsQuery request, CancellationToken cancellationToken)
    {
        var query = request.Input;
        if (query.IncludeInactive && !(_currentUser.IsInRole(Roles.Admin) || _currentUser.IsInRole(Roles.Manager)))
        {
            return AppErrors.Forbidden("Only managers can list inactive items.");
        }

        var items = _items.WithReferences().AsNoTracking().Where(i => query.IncludeInactive || i.IsActive);
        if (query.CategoryId is { } categoryId)
        {
            items = items.Where(i => i.CategoryId == categoryId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToUpperInvariant();
            items = items.Where(i => i.Name.ToUpper().Contains(term) || (i.Code != null && i.Code.Contains(term)));
        }

        var list = await items
            .OrderBy(i => i.Category!.SortOrder)
            .ThenBy(i => i.SortOrder)
            .ThenBy(i => i.Name)
            .ToListAsync(cancellationToken);
        return list.Select(MenuMapping.ToDto).ToList();
    }
}
