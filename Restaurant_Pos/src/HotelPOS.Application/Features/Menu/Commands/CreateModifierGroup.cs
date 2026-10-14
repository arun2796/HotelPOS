using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Menu;

namespace HotelPOS.Application.Features.Menu.Commands;

public sealed record CreateModifierGroupCommand(SaveModifierGroupRequest Input)
    : ICommand<ModifierGroupDto>, IHasInput<SaveModifierGroupRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.MenuManage;
}

// Also validates UpdateModifierGroupCommand, which takes the same DTO.
public sealed class SaveModifierGroupRequestValidator : AbstractValidator<SaveModifierGroupRequest>
{
    public SaveModifierGroupRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(MenuLimits.NameMaxLength);
        RuleFor(x => x.MinSelections).GreaterThanOrEqualTo(0);
        RuleFor(x => x.MaxSelections).InclusiveBetween(1, MenuLimits.MaxModifierSelections);
        RuleFor(x => x.MaxSelections).GreaterThanOrEqualTo(x => x.MinSelections)
            .WithMessage("The maximum must be at least the minimum.");
    }
}

internal sealed class CreateModifierGroupHandler : ICommandHandler<CreateModifierGroupCommand, ModifierGroupDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly MenuModifiers _modifiers;
    private readonly MenuChanges _changes;

    public CreateModifierGroupHandler(IAppDbContext db, IAuditTrail audit, MenuModifiers modifiers, MenuChanges changes)
    {
        _db = db;
        _audit = audit;
        _modifiers = modifiers;
        _changes = changes;
    }

    public async Task<Result<ModifierGroupDto>> Handle(CreateModifierGroupCommand command, CancellationToken cancellationToken)
    {
        var request = command.Input;
        if (await _modifiers.NameTakenAsync(request.Name, null, cancellationToken))
        {
            return AppErrors.Duplicate($"A modifier group named '{request.Name.Trim()}' already exists.");
        }

        var group = new ModifierGroup(request.Name, request.MinSelections, request.MaxSelections);
        _db.ModifierGroups.Add(group);
        await _db.SaveChangesAsync(cancellationToken);
        _audit.Add(AuditActions.ModifierGroupCreated, nameof(ModifierGroup), group.Id.ToString(),
            newValues: new { group.Name, group.MinSelections, group.MaxSelections });
        await _changes.SaveAsync(cancellationToken);
        return await _modifiers.GetAsync(group.Id, cancellationToken);
    }
}
