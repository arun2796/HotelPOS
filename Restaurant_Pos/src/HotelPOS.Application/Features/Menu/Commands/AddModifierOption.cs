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

public sealed record AddModifierOptionCommand(int GroupId, SaveModifierOptionRequest Input)
    : ICommand<ModifierGroupDto>, IHasInput<SaveModifierOptionRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.MenuManage;
}

// Also validates UpdateModifierOptionCommand, which takes the same DTO.
public sealed class SaveModifierOptionRequestValidator : AbstractValidator<SaveModifierOptionRequest>
{
    public SaveModifierOptionRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(MenuLimits.NameMaxLength);
        RuleFor(x => x.PriceDelta).InclusiveBetween(-MenuLimits.MaxPriceDelta, MenuLimits.MaxPriceDelta).ValidMoney();
        RuleFor(x => x.SortOrder).ValidSortOrder();
    }
}

internal sealed class AddModifierOptionHandler : ICommandHandler<AddModifierOptionCommand, ModifierGroupDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly MenuModifiers _modifiers;
    private readonly MenuChanges _changes;

    public AddModifierOptionHandler(IAppDbContext db, IAuditTrail audit, MenuModifiers modifiers, MenuChanges changes)
    {
        _db = db;
        _audit = audit;
        _modifiers = modifiers;
        _changes = changes;
    }

    public async Task<Result<ModifierGroupDto>> Handle(AddModifierOptionCommand command, CancellationToken cancellationToken)
    {
        var (groupId, request) = (command.GroupId, command.Input);
        if (!await _db.ModifierGroups.AnyAsync(g => g.Id == groupId, cancellationToken))
        {
            return AppErrors.NotFound("Modifier group", groupId);
        }

        if (await _modifiers.OptionNameTakenAsync(groupId, request.Name, null, cancellationToken))
        {
            return AppErrors.Duplicate($"The group already has an option named '{request.Name.Trim()}'.");
        }

        var option = new ModifierOption(groupId, request.Name, request.PriceDelta, request.SortOrder);
        _db.ModifierOptions.Add(option);
        await _db.SaveChangesAsync(cancellationToken);
        _audit.Add(AuditActions.ModifierOptionAdded, nameof(ModifierGroup), groupId.ToString(),
            newValues: new { OptionId = option.Id, option.Name, option.PriceDelta });
        await _changes.SaveAsync(cancellationToken);
        return await _modifiers.GetAsync(groupId, cancellationToken);
    }
}
