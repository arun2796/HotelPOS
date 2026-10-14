using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Security;

namespace HotelPOS.Application.Features.Menu.Queries;

public sealed record GetModifierGroupQuery(int Id) : IQuery<ModifierGroupDto>, IRequirePermission
{
    public string Permission => Permissions.MenuView;
}

internal sealed class GetModifierGroupHandler : IQueryHandler<GetModifierGroupQuery, ModifierGroupDto>
{
    private readonly MenuModifiers _modifiers;

    public GetModifierGroupHandler(MenuModifiers modifiers)
    {
        _modifiers = modifiers;
    }

    public Task<Result<ModifierGroupDto>> Handle(GetModifierGroupQuery request, CancellationToken cancellationToken) =>
        _modifiers.GetAsync(request.Id, cancellationToken);
}
