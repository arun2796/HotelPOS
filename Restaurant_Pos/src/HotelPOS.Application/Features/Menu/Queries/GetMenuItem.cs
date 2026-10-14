using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Security;

namespace HotelPOS.Application.Features.Menu.Queries;

public sealed record GetMenuItemQuery(int Id) : IQuery<MenuItemDto>, IRequirePermission
{
    public string Permission => Permissions.MenuView;
}

internal sealed class GetMenuItemHandler : IQueryHandler<GetMenuItemQuery, MenuItemDto>
{
    private readonly MenuItems _items;

    public GetMenuItemHandler(MenuItems items)
    {
        _items = items;
    }

    public Task<Result<MenuItemDto>> Handle(GetMenuItemQuery request, CancellationToken cancellationToken) =>
        _items.GetAsync(request.Id, cancellationToken);
}
