using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Menu;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Menu.Commands;

public sealed record DeactivateModifierGroupCommand(int Id) : ICommand<ModifierGroupDto>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.MenuManage;
}

internal sealed class DeactivateModifierGroupHandler : ICommandHandler<DeactivateModifierGroupCommand, ModifierGroupDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly MenuModifiers _modifiers;
    private readonly MenuChanges _changes;

    public DeactivateModifierGroupHandler(IAppDbContext db, IAuditTrail audit, MenuModifiers modifiers, MenuChanges changes)
    {
        _db = db;
        _audit = audit;
        _modifiers = modifiers;
        _changes = changes;
    }

    public async Task<Result<ModifierGroupDto>> Handle(DeactivateModifierGroupCommand command, CancellationToken cancellationToken)
    {
        var group = await _db.ModifierGroups.FirstOrDefaultAsync(g => g.Id == command.Id, cancellationToken);
        if (group is null)
        {
            return AppErrors.NotFound("Modifier group", command.Id);
        }

        if (group.IsActive)
        {
            // Items keep their link; inactive groups are simply left out of the ordering menu.
            group.Deactivate();
            _audit.Add(AuditActions.ModifierGroupDeactivated, nameof(ModifierGroup), group.Id.ToString());
            await _changes.SaveAsync(cancellationToken);
        }

        return await _modifiers.GetAsync(command.Id, cancellationToken);
    }
}
