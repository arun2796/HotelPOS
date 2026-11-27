using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Menu;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Menu;

// Steps shared by the modifier group and option commands and queries.
internal sealed class MenuModifiers
{
    private readonly IAppDbContext _db;

    public MenuModifiers(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<Result<ModifierGroupDto>> GetAsync(int id, CancellationToken cancellationToken)
    {
        var group = await _db.ModifierGroups.AsNoTracking().Include(g => g.Options).FirstOrDefaultAsync(g => g.Id == id, cancellationToken);
        return group is null ? AppErrors.NotFound("Modifier group", id) : MenuMapping.ToDto(group, includeInactiveOptions: true);
    }

    public Task<bool> NameTakenAsync(string name, int? excludingId, CancellationToken cancellationToken)
    {
        var normalized = name.Trim().ToUpperInvariant();
        return _db.ModifierGroups.AnyAsync(g => g.Name.ToUpper() == normalized && g.Id != excludingId, cancellationToken);
    }

    public Task<bool> OptionNameTakenAsync(int groupId, string name, int? excludingId, CancellationToken cancellationToken)
    {
        var normalized = name.Trim().ToUpperInvariant();
        return _db.ModifierOptions.AnyAsync(
            o => o.ModifierGroupId == groupId && o.Name.ToUpper() == normalized && o.Id != excludingId, cancellationToken);
    }

    // A modifier group with 0 active options (or fewer than MinSelections) has no valid choice to offer waiters.
    public static bool CanOffer(int activeOptionCount, int minSelections) =>
        activeOptionCount >= Math.Max(1, minSelections);

    public async Task<AppError?> CheckOptionDeactivationAsync(int groupId, int optionId, CancellationToken cancellationToken)
    {
        var group = await _db.ModifierGroups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken);
        if (group is null || !group.IsActive || group.MinSelections <= 0)
        {
            return null;
        }

        var remainingActive = await _db.ModifierOptions.CountAsync(
            o => o.ModifierGroupId == groupId && o.IsActive && o.Id != optionId,
            cancellationToken);
        return await CheckLinkedMinimumAsync(groupId, group.Name, group.MinSelections, remainingActive, cancellationToken);
    }

    public async Task<AppError?> CheckLinkedMinimumAsync(
        int groupId,
        string groupName,
        int minSelections,
        int activeOptionCount,
        CancellationToken cancellationToken)
    {
        if (minSelections <= activeOptionCount)
        {
            return null;
        }

        var inUse = await _db.MenuItemModifierGroups.AnyAsync(
            l => l.ModifierGroupId == groupId && l.MenuItem!.IsActive,
            cancellationToken);
        return inUse
            ? AppErrors.BusinessRule(
                $"Modifier group '{groupName}' is attached to active menu items and needs at least {minSelections} active option(s) (currently {activeOptionCount}).")
            : null;
    }
}
