using System.Globalization;
using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Menu;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Menu.Commands;

// Prepared food thrown away (waste) or a counted correction. Both are explicit, need a reason and are one ledger row;
// the count can never go below zero.
public sealed record AdjustMenuItemStockCommand(int Id, AdjustStockRequest Input)
    : ICommand<MenuItemDto>, IHasInput<AdjustStockRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.StockAdjust;
}

public sealed class AdjustStockRequestValidator : AbstractValidator<AdjustStockRequest>
{
    public AdjustStockRequestValidator()
    {
        RuleFor(x => x.Kind).Must(k => k is StockMovementType.Waste or StockMovementType.Adjustment).WithMessage("Choose waste or adjustment.");
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Say why (for example: dropped, expired, counted again).")
            .MaximumLength(StockLimits.ReasonMaxLength);
        RuleFor(x => x.Quantity).InclusiveBetween(1, StockLimits.MaxChange).When(x => x.Kind == StockMovementType.Waste)
            .WithMessage("Enter how many portions were lost.");
        RuleFor(x => x.Quantity).InclusiveBetween(-StockLimits.MaxChange, StockLimits.MaxChange).NotEqual(0).When(x => x.Kind == StockMovementType.Adjustment)
            .WithMessage("Enter the change (negative removes, positive adds).");
    }
}

internal sealed class AdjustMenuItemStockHandler : ICommandHandler<AdjustMenuItemStockCommand, MenuItemDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly MenuItems _items;
    private readonly MenuChanges _changes;

    public AdjustMenuItemStockHandler(IAppDbContext db, IAuditTrail audit, MenuItems items, MenuChanges changes)
    {
        _db = db;
        _audit = audit;
        _items = items;
        _changes = changes;
    }

    public async Task<Result<MenuItemDto>> Handle(AdjustMenuItemStockCommand command, CancellationToken cancellationToken)
    {
        var request = command.Input;
        var id = command.Id;
        var item = await _db.MenuItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null)
        {
            return AppErrors.NotFound("Menu item", id);
        }

        if (!item.IsStockTracked)
        {
            return AppErrors.BusinessRule($"{item.Name} has no stock count. Set one first.");
        }

        var delta = request.Kind == StockMovementType.Waste ? -request.Quantity : request.Quantity;

        // One conditional update: it never takes the count below zero, even when an order is being sent at the same moment.
        var rows = await _db.MenuItems
            .Where(m => m.Id == id && m.StockMode != StockTrackingMode.None && m.StockRemaining != null && m.StockRemaining + delta >= 0)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.IsAvailable, m => m.StockRemaining + delta > 0)
                .SetProperty(m => m.StockRemaining, m => m.StockRemaining + delta), cancellationToken);
        var after = await _db.MenuItems.AsNoTracking().Where(m => m.Id == id).Select(m => m.StockRemaining ?? 0).FirstAsync(cancellationToken);
        if (rows == 0)
        {
            return AppErrors.BusinessRule($"Only {after} {item.Name} left, so {-delta} cannot be taken off.");
        }

        _db.StockMovements.Add(new StockMovement(id, request.Kind, after - delta, delta, after, null, request.Reason));
        _audit.Add(AuditActions.MenuItemStockAdjusted, nameof(MenuItem), id.ToString(CultureInfo.InvariantCulture),
            new { Remaining = after - delta }, new { Remaining = after, request.Kind, request.Reason });
        await _changes.SaveAsync(cancellationToken);
        return await _items.GetAsync(id, cancellationToken);
    }
}
