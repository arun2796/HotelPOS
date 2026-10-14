using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Menu;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Menu.Commands;

public sealed record DeactivateMenuItemCommand(int Id) : ICommand<MenuItemDto>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.MenuManage;
}

internal sealed class DeactivateMenuItemHandler : ICommandHandler<DeactivateMenuItemCommand, MenuItemDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly MenuItems _items;

    public DeactivateMenuItemHandler(IAppDbContext db, IAuditTrail audit, MenuItems items)
    {
        _db = db;
        _audit = audit;
        _items = items;
    }

    public async Task<Result<MenuItemDto>> Handle(DeactivateMenuItemCommand command, CancellationToken cancellationToken)
    {
        var item = await _db.MenuItems.FirstOrDefaultAsync(i => i.Id == command.Id, cancellationToken);
        if (item is null)
        {
            return AppErrors.NotFound("Menu item", command.Id);
        }

        if (!item.IsActive)
        {
            return await _items.GetAsync(command.Id, cancellationToken);
        }

        item.Deactivate();
        _audit.Add(AuditActions.MenuItemDeactivated, nameof(MenuItem), item.Id.ToString());
        return await _items.SaveAsync(command.Id, cancellationToken);
    }
}
