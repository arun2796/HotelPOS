using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Menu;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Menu.Commands;

public sealed record DeactivateModifierOptionCommand(int GroupId, int OptionId) : ICommand<ModifierGroupDto>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.MenuManage;
}

internal sealed class DeactivateModifierOptionHandler : ICommandHandler<DeactivateModifierOptionCommand, ModifierGroupDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly MenuModifiers _modifiers;
    private readonly MenuChanges _changes;

    public DeactivateModifierOptionHandler(IAppDbContext db, IAuditTrail audit, MenuModifiers modifiers, MenuChanges changes)
    {
        _db = db;
        _audit = audit;
        _modifiers = modifiers;
        _changes = changes;
    }

    public async Task<Result<ModifierGroupDto>> Handle(DeactivateModifierOptionCommand command, CancellationToken cancellationToken)
    {
        var (groupId, optionId) = (command.GroupId, command.OptionId);
        var option = await _db.ModifierOptions.FirstOrDefaultAsync(o => o.Id == optionId && o.ModifierGroupId == groupId, cancellationToken);
        if (option is null)
        {
            return AppErrors.NotFound("Modifier option", optionId);
        }

        if (option.IsActive)
        {
            option.Deactivate();
            _audit.Add(AuditActions.ModifierOptionDeactivated, nameof(ModifierGroup), groupId.ToString(),
                newValues: new { OptionId = option.Id, option.Name });
            await _changes.SaveAsync(cancellationToken);
        }

        return await _modifiers.GetAsync(groupId, cancellationToken);
    }
}
