using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Menu;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Menu.Commands;

public sealed record UpdateModifierGroupCommand(int Id, SaveModifierGroupRequest Input)
    : ICommand<ModifierGroupDto>, IHasInput<SaveModifierGroupRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.MenuManage;
}

internal sealed class UpdateModifierGroupHandler : ICommandHandler<UpdateModifierGroupCommand, ModifierGroupDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly MenuModifiers _modifiers;
    private readonly MenuChanges _changes;

    public UpdateModifierGroupHandler(IAppDbContext db, IAuditTrail audit, MenuModifiers modifiers, MenuChanges changes)
    {
        _db = db;
        _audit = audit;
        _modifiers = modifiers;
        _changes = changes;
    }

    public async Task<Result<ModifierGroupDto>> Handle(UpdateModifierGroupCommand command, CancellationToken cancellationToken)
    {
        var (id, request) = (command.Id, command.Input);
        var group = await _db.ModifierGroups.FirstOrDefaultAsync(g => g.Id == id, cancellationToken);
        if (group is null)
        {
            return AppErrors.NotFound("Modifier group", id);
        }

        if (await _modifiers.NameTakenAsync(request.Name, id, cancellationToken))
        {
            return AppErrors.Duplicate($"A modifier group named '{request.Name.Trim()}' already exists.");
        }

        var oldValues = new { group.Name, group.MinSelections, group.MaxSelections, group.IsActive };
        group.Update(request.Name, request.MinSelections, request.MaxSelections);
        if (request.IsActive)
        {
            group.Activate();
        }
        else
        {
            group.Deactivate();
        }

        _audit.Add(AuditActions.ModifierGroupUpdated, nameof(ModifierGroup), group.Id.ToString(),
            oldValues, new { group.Name, group.MinSelections, group.MaxSelections, group.IsActive });
        await _changes.SaveAsync(cancellationToken);
        return await _modifiers.GetAsync(id, cancellationToken);
    }
}
