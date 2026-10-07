using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Menu;
using HotelPOS.Domain.Menu;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Menu;

public interface IModifierService
{
    Task<IReadOnlyList<ModifierGroupDto>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default);

    Task<Result<ModifierGroupDto>> GetAsync(int id, CancellationToken cancellationToken = default);

    Task<Result<ModifierGroupDto>> CreateAsync(SaveModifierGroupRequest request, CancellationToken cancellationToken = default);

    Task<Result<ModifierGroupDto>> UpdateAsync(int id, SaveModifierGroupRequest request, CancellationToken cancellationToken = default);

    Task<Result<ModifierGroupDto>> DeactivateAsync(int id, CancellationToken cancellationToken = default);

    Task<Result<ModifierGroupDto>> AddOptionAsync(int groupId, SaveModifierOptionRequest request, CancellationToken cancellationToken = default);

    Task<Result<ModifierGroupDto>> UpdateOptionAsync(int groupId, int optionId, SaveModifierOptionRequest request, CancellationToken cancellationToken = default);

    Task<Result<ModifierGroupDto>> DeactivateOptionAsync(int groupId, int optionId, CancellationToken cancellationToken = default);
}

public sealed class ModifierService : IModifierService
{
    private readonly IAppDbContext _db;
    private readonly IAuditService _audit;
    private readonly MenuChanges _changes;

    public ModifierService(IAppDbContext db, IAuditService audit, MenuChanges changes)
    {
        _db = db;
        _audit = audit;
        _changes = changes;
    }

    public async Task<IReadOnlyList<ModifierGroupDto>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default)
    {
        var groups = await _db.ModifierGroups.AsNoTracking()
            .Include(g => g.Options)
            .Where(g => includeInactive || g.IsActive)
            .OrderBy(g => g.Name)
            .ToListAsync(cancellationToken);
        return groups.Select(g => ToDto(g, includeInactive)).ToList();
    }

    public async Task<Result<ModifierGroupDto>> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var group = await _db.ModifierGroups.AsNoTracking().Include(g => g.Options).FirstOrDefaultAsync(g => g.Id == id, cancellationToken);
        return group is null ? AppErrors.NotFound("Modifier group", id) : ToDto(group, includeInactiveOptions: true);
    }

    public async Task<Result<ModifierGroupDto>> CreateAsync(SaveModifierGroupRequest request, CancellationToken cancellationToken = default)
    {
        if (await NameTakenAsync(request.Name, null, cancellationToken))
        {
            return AppErrors.Duplicate($"A modifier group named '{request.Name.Trim()}' already exists.");
        }

        var group = new ModifierGroup(request.Name, request.MinSelections, request.MaxSelections);
        await using var transaction = await _changes.BeginAsync(cancellationToken);
        _db.ModifierGroups.Add(group);
        await _db.SaveChangesAsync(cancellationToken);
        _audit.Record(AuditActions.ModifierGroupCreated, nameof(ModifierGroup), group.Id.ToString(),
            newValues: new { group.Name, group.MinSelections, group.MaxSelections });
        await _changes.CommitAsync(transaction, cancellationToken);
        return await GetAsync(group.Id, cancellationToken);
    }

    public async Task<Result<ModifierGroupDto>> UpdateAsync(int id, SaveModifierGroupRequest request, CancellationToken cancellationToken = default)
    {
        var group = await _db.ModifierGroups.FirstOrDefaultAsync(g => g.Id == id, cancellationToken);
        if (group is null)
        {
            return AppErrors.NotFound("Modifier group", id);
        }

        if (await NameTakenAsync(request.Name, id, cancellationToken))
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

        await using var transaction = await _changes.BeginAsync(cancellationToken);
        _audit.Record(AuditActions.ModifierGroupUpdated, nameof(ModifierGroup), group.Id.ToString(),
            oldValues, new { group.Name, group.MinSelections, group.MaxSelections, group.IsActive });
        await _changes.CommitAsync(transaction, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<Result<ModifierGroupDto>> DeactivateAsync(int id, CancellationToken cancellationToken = default)
    {
        var group = await _db.ModifierGroups.FirstOrDefaultAsync(g => g.Id == id, cancellationToken);
        if (group is null)
        {
            return AppErrors.NotFound("Modifier group", id);
        }

        if (group.IsActive)
        {
            // Items keep their link; inactive groups are simply left out of the ordering menu.
            await using var transaction = await _changes.BeginAsync(cancellationToken);
            group.Deactivate();
            _audit.Record(AuditActions.ModifierGroupDeactivated, nameof(ModifierGroup), group.Id.ToString());
            await _changes.CommitAsync(transaction, cancellationToken);
        }

        return await GetAsync(id, cancellationToken);
    }

    public async Task<Result<ModifierGroupDto>> AddOptionAsync(int groupId, SaveModifierOptionRequest request, CancellationToken cancellationToken = default)
    {
        if (!await _db.ModifierGroups.AnyAsync(g => g.Id == groupId, cancellationToken))
        {
            return AppErrors.NotFound("Modifier group", groupId);
        }

        if (await OptionNameTakenAsync(groupId, request.Name, null, cancellationToken))
        {
            return AppErrors.Duplicate($"The group already has an option named '{request.Name.Trim()}'.");
        }

        var option = new ModifierOption(groupId, request.Name, request.PriceDelta, request.SortOrder);
        await using var transaction = await _changes.BeginAsync(cancellationToken);
        _db.ModifierOptions.Add(option);
        await _db.SaveChangesAsync(cancellationToken);
        _audit.Record(AuditActions.ModifierOptionAdded, nameof(ModifierGroup), groupId.ToString(),
            newValues: new { OptionId = option.Id, option.Name, option.PriceDelta });
        await _changes.CommitAsync(transaction, cancellationToken);
        return await GetAsync(groupId, cancellationToken);
    }

    public async Task<Result<ModifierGroupDto>> UpdateOptionAsync(int groupId, int optionId, SaveModifierOptionRequest request, CancellationToken cancellationToken = default)
    {
        var option = await _db.ModifierOptions.FirstOrDefaultAsync(o => o.Id == optionId && o.ModifierGroupId == groupId, cancellationToken);
        if (option is null)
        {
            return AppErrors.NotFound("Modifier option", optionId);
        }

        if (await OptionNameTakenAsync(groupId, request.Name, optionId, cancellationToken))
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

        await using var transaction = await _changes.BeginAsync(cancellationToken);
        _audit.Record(AuditActions.ModifierOptionUpdated, nameof(ModifierGroup), groupId.ToString(),
            oldValues, new { OptionId = option.Id, option.Name, option.PriceDelta, option.IsActive });
        await _changes.CommitAsync(transaction, cancellationToken);
        return await GetAsync(groupId, cancellationToken);
    }

    public async Task<Result<ModifierGroupDto>> DeactivateOptionAsync(int groupId, int optionId, CancellationToken cancellationToken = default)
    {
        var option = await _db.ModifierOptions.FirstOrDefaultAsync(o => o.Id == optionId && o.ModifierGroupId == groupId, cancellationToken);
        if (option is null)
        {
            return AppErrors.NotFound("Modifier option", optionId);
        }

        if (option.IsActive)
        {
            await using var transaction = await _changes.BeginAsync(cancellationToken);
            option.Deactivate();
            _audit.Record(AuditActions.ModifierOptionDeactivated, nameof(ModifierGroup), groupId.ToString(),
                newValues: new { OptionId = option.Id, option.Name });
            await _changes.CommitAsync(transaction, cancellationToken);
        }

        return await GetAsync(groupId, cancellationToken);
    }

    private static ModifierGroupDto ToDto(ModifierGroup g, bool includeInactiveOptions) => new()
    {
        Id = g.Id,
        Name = g.Name,
        MinSelections = g.MinSelections,
        MaxSelections = g.MaxSelections,
        IsActive = g.IsActive,
        Options = g.Options
            .Where(o => includeInactiveOptions || o.IsActive)
            .OrderBy(o => o.SortOrder)
            .ThenBy(o => o.Name)
            .Select(o => new ModifierOptionDto
            {
                Id = o.Id,
                ModifierGroupId = o.ModifierGroupId,
                Name = o.Name,
                PriceDelta = o.PriceDelta,
                SortOrder = o.SortOrder,
                IsActive = o.IsActive,
            })
            .ToList(),
    };

    private Task<bool> NameTakenAsync(string name, int? excludingId, CancellationToken cancellationToken)
    {
        var normalized = name.Trim().ToUpperInvariant();
        return _db.ModifierGroups.AnyAsync(g => g.Name.ToUpper() == normalized && g.Id != excludingId, cancellationToken);
    }

    private Task<bool> OptionNameTakenAsync(int groupId, string name, int? excludingId, CancellationToken cancellationToken)
    {
        var normalized = name.Trim().ToUpperInvariant();
        return _db.ModifierOptions.AnyAsync(
            o => o.ModifierGroupId == groupId && o.Name.ToUpper() == normalized && o.Id != excludingId, cancellationToken);
    }
}
