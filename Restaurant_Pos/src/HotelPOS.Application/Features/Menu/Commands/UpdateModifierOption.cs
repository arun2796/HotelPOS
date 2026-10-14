using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Menu;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Menu.Commands;

public sealed record UpdateModifierOptionCommand(int GroupId, int OptionId, SaveModifierOptionRequest Input)
    : ICommand<ModifierGroupDto>, IHasInput<SaveModifierOptionRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.MenuManage;
}

internal sealed class UpdateModifierOptionHandler : ICommandHandler<UpdateModifierOptionCommand, ModifierGroupDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly MenuModifiers _modifiers;
    private readonly MenuChanges _changes;

    public UpdateModifierOptionHandler(IAppDbContext db, IAuditTrail audit, MenuModifiers modifiers, MenuChanges changes)
    {
        _db = db;
        _audit = audit;
        _modifiers = modifiers;
        _changes = changes;
    }

    public async Task<Result<ModifierGroupDto>> Handle(UpdateModifierOptionCommand command, CancellationToken cancellationToken)
    {
        var (groupId, optionId, request) = (command.GroupId, command.OptionId, command.Input);
        var option = await _db.ModifierOptions.FirstOrDefaultAsync(o => o.Id == optionId && o.ModifierGroupId == groupId, cancellationToken);
        if (option is null)
        {
            return AppErrors.NotFound("Modifier option", optionId);
        }

        if (await _modifiers.OptionNameTakenAsync(groupId, request.Name, optionId, cancellationToken))
        {
            return AppErrors.Duplicate($"The group already has an option named '{request.Name.Trim()}'.");
        }

        var oldValues = new { OptionId = option.Id, option.Name, option.PriceDelta, option.IsActive };
        option.Update(request.Name, request.PriceDelta, request.SortOrder);
        if (request.IsActive)
        {
            option.Activate();
        }
        else
        {
            option.Deactivate();
        }

        _audit.Add(AuditActions.ModifierOptionUpdated, nameof(ModifierGroup), groupId.ToString(),
            oldValues, new { OptionId = option.Id, option.Name, option.PriceDelta, option.IsActive });
        await _changes.SaveAsync(cancellationToken);
        return await _modifiers.GetAsync(groupId, cancellationToken);
    }
}
