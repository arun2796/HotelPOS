using System.Globalization;
using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Common;
using HotelPOS.Domain.Menu;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Menu.Commands;

// The count at or below which an item shows as Low stock on every screen.
public sealed record SetReorderLevelCommand(int Id, SetReorderLevelRequest Input)
    : ICommand<MenuItemDto>, IHasInput<SetReorderLevelRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.MenuAvailability;
}

public sealed class SetReorderLevelRequestValidator : AbstractValidator<SetReorderLevelRequest>
{
    public SetReorderLevelRequestValidator()
    {
        RuleFor(x => x.ReorderLevel).InclusiveBetween(0, StockLimits.MaxChange).When(x => x.ReorderLevel is not null);
    }
}

internal sealed class SetReorderLevelHandler : ICommandHandler<SetReorderLevelCommand, MenuItemDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly MenuItems _items;
    private readonly MenuChanges _changes;

    public SetReorderLevelHandler(IAppDbContext db, IAuditTrail audit, MenuItems items, MenuChanges changes)
    {
        _db = db;
        _audit = audit;
        _items = items;
        _changes = changes;
    }

    public async Task<Result<MenuItemDto>> Handle(SetReorderLevelCommand command, CancellationToken cancellationToken)
    {
        var item = await _db.MenuItems.FirstOrDefaultAsync(i => i.Id == command.Id, cancellationToken);
        if (item is null)
        {
            return AppErrors.NotFound("Menu item", command.Id);
        }

        var before = item.ReorderLevel;
        try
        {
            item.SetReorderLevel(command.Input.ReorderLevel);
        }
        catch (DomainException ex)
        {
            return AppErrors.BusinessRule(ex.Message);
        }

        _db.Entry(item).State = EntityState.Modified;
        _audit.Add(AuditActions.MenuItemStockChanged, nameof(MenuItem), item.Id.ToString(CultureInfo.InvariantCulture),
            new { ReorderLevel = before }, new { item.ReorderLevel });
        await _changes.SaveAsync(cancellationToken);
        return await _items.GetAsync(command.Id, cancellationToken);
    }
}
