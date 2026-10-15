using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Security;

namespace HotelPOS.Application.Features.Menu.Queries;

public sealed record GetCategoryQuery(int Id) : IQuery<CategoryDto>, IRequirePermission
{
    public string Permission => Permissions.MenuView;
}

internal sealed class GetCategoryHandler : IQueryHandler<GetCategoryQuery, CategoryDto>
{
    private readonly MenuCategories _categories;

    public GetCategoryHandler(MenuCategories categories)
    {
        _categories = categories;
    }

    public Task<Result<CategoryDto>> Handle(GetCategoryQuery request, CancellationToken cancellationToken) =>
        _categories.GetAsync(request.Id, cancellationToken);
}
