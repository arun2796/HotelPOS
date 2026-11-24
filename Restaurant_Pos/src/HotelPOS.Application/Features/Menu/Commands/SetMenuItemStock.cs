using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Common;
using HotelPOS.Domain.Menu;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Menu.Commands;

// The kitchen supervisor loads a batch ("4 vessels = 200 idlis"), tops it up, or stops counting. The change goes out
// to every captain's menu at once; an item at zero is already greyed out there.
public sealed record SetMenuItemStockCommand(int Id, SetStockRequest Input)
    : ICommand<MenuItemDto>, IHasInput<SetStockRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.MenuAvailability;
}

public sealed class SetStockRequestValidator : AbstractValidator<SetStockRequest>
{
    public SetStockRequestValidator()
    {
        RuleFor(x => x.Mode).IsInEnum();
        RuleFor(x => x.Quantity).InclusiveBetween(0, 100000).When(x => x.Mode != StockTrackingMode.None);
        RuleFor(x => x.Quantity).NotNull().When(x => x.Mode != StockTrackingMode.None).WithMessage("Enter the stock count.");
    }
}

internal sealed class SetMenuItemStockHandler : ICommandHandler<SetMenuItemStockCommand, MenuItemDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly MenuItems _items;
    private readonly MenuChanges _changes;

    public SetMenuItemStockHandler(IAppDbContext db, IAuditTrail audit, MenuItems items, MenuChanges changes)
    {
        _db = db;
        _audit = audit;
        _items = items;
        _changes = changes;
    }

    public async Task<Result<MenuItemDto>> Handle(SetMenuItemStockCommand command, CancellationToken cancellationToken)
    {
        var request = command.Input;
        var item = await _db.MenuItems.FirstOrDefaultAsync(i => i.Id == command.Id, cancellationToken);
        if (item is null)
        {
            return AppErrors.NotFound("Menu item", command.Id);
        }

        var before = new { item.StockMode, item.StockOpening, item.StockRemaining, item.IsAvailable };
        try
        {
            if (request.Add && request.Mode != StockTrackingMode.None)
            {
                item.AddStock(request.Quantity ?? 0);
            }
            else
            {
                item.ConfigureStock(request.Mode, request.Quantity);
            }
        }
        catch (DomainException ex)
        {
            return AppErrors.BusinessRule(ex.Message);
        }

        _db.Entry(item).State = EntityState.Modified;
        _audit.Add(AuditActions.MenuItemStockChanged, nameof(MenuItem), item.Id.ToString(), before,
            new { item.StockMode, item.StockOpening, item.StockRemaining, item.IsAvailable });
        try
        {
            await _changes.SaveAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return await _items.ConcurrencyAsync(command.Id, cancellationToken);
        }

        return await _items.GetAsync(command.Id, cancellationToken);
    }
}
