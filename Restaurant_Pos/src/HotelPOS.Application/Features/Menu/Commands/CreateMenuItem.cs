using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Menu;

namespace HotelPOS.Application.Features.Menu.Commands;

public sealed record CreateMenuItemCommand(CreateMenuItemRequest Input)
    : ICommand<MenuItemDto>, IHasInput<CreateMenuItemRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.MenuManage;
}

public sealed class CreateMenuItemRequestValidator : AbstractValidator<CreateMenuItemRequest>
{
    public CreateMenuItemRequestValidator()
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
    }
}

internal sealed class CreateMenuItemHandler : ICommandHandler<CreateMenuItemCommand, MenuItemDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly MenuItems _items;
    private readonly MenuChanges _changes;

    public CreateMenuItemHandler(IAppDbContext db, IAuditTrail audit, MenuItems items, MenuChanges changes)
    {
        _db = db;
        _audit = audit;
        _items = items;
        _changes = changes;
    }

    public async Task<Result<MenuItemDto>> Handle(CreateMenuItemCommand command, CancellationToken cancellationToken)
    {
        var request = command.Input;
        var error = await _items.CheckReferencesAsync(request.CategoryId, request.TaxId, request.PreparationStationId, request.ModifierGroupIds, cancellationToken)
            ?? await _items.CheckUniqueAsync(request.CategoryId, request.Name, request.Code, null, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var item = new MenuItem(request.CategoryId, request.Name, request.Code, request.Description, request.Price,
            request.TaxId, request.PreparationStationId, request.SortOrder);
        item.SetAvailability(request.IsAvailable);
        item.SetModifierGroups(request.ModifierGroupIds);
        item.SetHsnSac(request.HsnSac);

        _db.MenuItems.Add(item);
        await _db.SaveChangesAsync(cancellationToken);
        _audit.Add(AuditActions.MenuItemCreated, nameof(MenuItem), item.Id.ToString(),
            newValues: new { item.Name, item.Code, item.CategoryId, item.Price, item.TaxId, item.PreparationStationId });
        await _changes.SaveAsync(cancellationToken);
        return await _items.GetAsync(item.Id, cancellationToken);
    }
}
