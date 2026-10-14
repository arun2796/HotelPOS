using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Menu;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Menu.Commands;

// Sold out / available again, from the kitchen as well as from managers.
public sealed record SetMenuItemAvailabilityCommand(int Id, SetAvailabilityRequest Input)
    : ICommand<MenuItemDto>, IHasInput<SetAvailabilityRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.MenuAvailability;
}

internal sealed class SetMenuItemAvailabilityHandler : ICommandHandler<SetMenuItemAvailabilityCommand, MenuItemDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly MenuItems _items;

    public SetMenuItemAvailabilityHandler(IAppDbContext db, IAuditTrail audit, MenuItems items)
    {
        _db = db;
        _audit = audit;
        _items = items;
    }

    public async Task<Result<MenuItemDto>> Handle(SetMenuItemAvailabilityCommand command, CancellationToken cancellationToken)
    {
        var isAvailable = command.Input.IsAvailable;
        var item = await _db.MenuItems.FirstOrDefaultAsync(i => i.Id == command.Id, cancellationToken);
        if (item is null)
        {
            return AppErrors.NotFound("Menu item", command.Id);
        }

        if (item.IsAvailable == isAvailable)
        {
            return await _items.GetAsync(command.Id, cancellationToken);
        }

        item.SetAvailability(isAvailable);
        _audit.Add(AuditActions.MenuItemAvailabilityChanged, nameof(MenuItem), item.Id.ToString(),
            new { IsAvailable = !isAvailable }, new { IsAvailable = isAvailable });
        return await _items.SaveAsync(command.Id, cancellationToken);
    }
}
