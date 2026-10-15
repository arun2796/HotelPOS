using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Menu;
using HotelPOS.Domain.Menu;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Menu;

// Steps shared by the menu item commands and queries.
internal sealed class MenuItems
{
    private readonly IAppDbContext _db;
    private readonly MenuChanges _changes;

    public MenuItems(IAppDbContext db, MenuChanges changes)
    {
        _db = db;
        _changes = changes;
    }

    public IQueryable<MenuItem> WithReferences() =>
        _db.MenuItems
            .Include(i => i.Category)
            .Include(i => i.Tax)
            .Include(i => i.PreparationStation)
            .Include(i => i.ModifierGroups);

    public async Task<Result<MenuItemDto>> GetAsync(int id, CancellationToken cancellationToken)
    {
        var item = await WithReferences().AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        return item is null ? AppErrors.NotFound("Menu item", id) : MenuMapping.ToDto(item);
    }

    public async Task<Result<MenuItemDto>> SaveAsync(int id, CancellationToken cancellationToken)
    {
        try
        {
            await _changes.SaveAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return await ConcurrencyAsync(id, cancellationToken);
        }

        return await GetAsync(id, cancellationToken);
    }

    public async Task<Result<MenuItemDto>> ConcurrencyAsync(int id, CancellationToken cancellationToken)
    {
        var current = await GetAsync(id, cancellationToken);
        return current.IsSuccess ? AppErrors.Concurrency("menu item", current.Value) : current;
    }

    public async Task<AppError?> CheckReferencesAsync(int categoryId, int? taxId, int stationId, IReadOnlyList<int> groupIds, CancellationToken cancellationToken)
    {
        if (!await _db.Categories.AnyAsync(c => c.Id == categoryId && c.IsActive, cancellationToken))
        {
            return AppErrors.Validation("categoryId", "Select an active category.");
        }

        if (!await _db.PreparationStations.AnyAsync(s => s.Id == stationId && s.IsActive, cancellationToken))
        {
            return AppErrors.Validation("preparationStationId", "Select an active preparation station.");
        }

        if (taxId is { } tax && !await _db.Taxes.AnyAsync(t => t.Id == tax && t.IsActive, cancellationToken))
        {
            return AppErrors.Validation("taxId", "Select an active tax, or none.");
        }

        var distinct = groupIds.Distinct().ToList();
        if (distinct.Count != groupIds.Count)
        {
            return AppErrors.Validation("modifierGroupIds", "A modifier group can be linked only once.");
        }

        var found = await _db.ModifierGroups.CountAsync(g => distinct.Contains(g.Id) && g.IsActive, cancellationToken);
        return found == distinct.Count ? null : AppErrors.Validation("modifierGroupIds", "Select active modifier groups only.");
    }

    public async Task<AppError?> CheckUniqueAsync(int categoryId, string name, string? code, int? excludingId, CancellationToken cancellationToken)
    {
        var normalizedName = name.Trim().ToUpperInvariant();
        if (await _db.MenuItems.AnyAsync(i => i.CategoryId == categoryId && i.Name.ToUpper() == normalizedName && i.Id != excludingId, cancellationToken))
        {
            return AppErrors.Duplicate($"The category already has an item named '{name.Trim()}'.");
        }

        var normalizedCode = MenuItem.NormalizeCode(code);
        if (normalizedCode is not null && await _db.MenuItems.AnyAsync(i => i.Code == normalizedCode && i.Id != excludingId, cancellationToken))
        {
            return AppErrors.Duplicate($"Item code '{normalizedCode}' is already used.");
        }

        return null;
    }
}
