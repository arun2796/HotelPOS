using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Menu;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Menu.Commands;

public sealed record UpdateMenuItemCommand(int Id, UpdateMenuItemRequest Input)
    : ICommand<MenuItemDto>, IHasInput<UpdateMenuItemRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.MenuManage;
}

public sealed class UpdateMenuItemRequestValidator : AbstractValidator<UpdateMenuItemRequest>
{
    public UpdateMenuItemRequestValidator()
    {
        RuleFor(x => x.CategoryId).GreaterThan(0).WithMessage("Select a category.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(MenuLimits.NameMaxLength);
        RuleFor(x => x.Code).ValidMenuCode();
        RuleFor(x => x.Description).MaximumLength(MenuLimits.DescriptionMaxLength);
        RuleFor(x => x.Price).InclusiveBetween(0m, MenuLimits.MaxPrice).ValidMoney();
        RuleFor(x => x.TaxId).GreaterThan(0).When(x => x.TaxId is not null);
        RuleFor(x => x.PreparationStationId).GreaterThan(0).WithMessage("Select a preparation station.");
        RuleFor(x => x.SortOrder).ValidSortOrder();
        RuleFor(x => x.HsnSac).Matches(@"^[0-9]{4,8}$").When(x => !string.IsNullOrWhiteSpace(x.HsnSac)).WithMessage("An HSN/SAC code is 4 to 8 digits.");
        RuleFor(x => x.ModifierGroupIds).NotNull();
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

internal sealed class UpdateMenuItemHandler : ICommandHandler<UpdateMenuItemCommand, MenuItemDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly MenuItems _items;

    public UpdateMenuItemHandler(IAppDbContext db, IAuditTrail audit, MenuItems items)
    {
        _db = db;
        _audit = audit;
        _items = items;
    }

    public async Task<Result<MenuItemDto>> Handle(UpdateMenuItemCommand command, CancellationToken cancellationToken)
    {
        var (id, request) = (command.Id, command.Input);
        var item = await _db.MenuItems.Include(i => i.ModifierGroups).FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null)
        {
            return AppErrors.NotFound("Menu item", id);
        }

        if (!RowVersions.Matches(item.RowVersion, request.RowVersion))
        {
            return await _items.ConcurrencyAsync(id, cancellationToken);
        }

        var error = await _items.CheckReferencesAsync(request.CategoryId, request.TaxId, request.PreparationStationId, request.ModifierGroupIds, cancellationToken)
            ?? await _items.CheckUniqueAsync(request.CategoryId, request.Name, request.Code, id, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var oldPrice = item.Price;
        var oldValues = new { item.Name, item.Code, item.CategoryId, item.Price, item.TaxId, item.PreparationStationId, item.IsAvailable, item.IsActive };
        item.Update(request.CategoryId, request.Name, request.Code, request.Description, request.Price,
            request.TaxId, request.PreparationStationId, request.SortOrder);
        item.SetAvailability(request.IsAvailable);
        item.SetModifierGroups(request.ModifierGroupIds);
        item.SetHsnSac(request.HsnSac);
        if (request.IsActive)
        {
            item.Activate();
        }
        else
        {
            item.Deactivate();
        }

        // Always write the item row (even when only modifier links changed) so its row version is checked and bumped.
        _db.Entry(item).State = EntityState.Modified;
        _audit.Add(AuditActions.MenuItemUpdated, nameof(MenuItem), item.Id.ToString(), oldValues,
            new { item.Name, item.Code, item.CategoryId, item.Price, item.TaxId, item.PreparationStationId, item.IsAvailable, item.IsActive });
        if (oldPrice != item.Price)
        {
            _audit.Add(AuditActions.MenuItemPriceChanged, nameof(MenuItem), item.Id.ToString(),
                new { Price = oldPrice }, new { item.Price });
        }

        return await _items.SaveAsync(item.Id, cancellationToken);
    }
}
