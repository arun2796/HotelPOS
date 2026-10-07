using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Contracts.Menu;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Menu;

public interface IMenuQuery
{
    Task<MenuDto> GetMenuAsync(int? knownVersion, CancellationToken cancellationToken = default);
}

public sealed class MenuQuery : IMenuQuery
{
    private readonly IAppDbContext _db;
    private readonly IMenuVersionStore _versions;

    public MenuQuery(IAppDbContext db, IMenuVersionStore versions)
    {
        _db = db;
        _versions = versions;
    }

    public async Task<MenuDto> GetMenuAsync(int? knownVersion, CancellationToken cancellationToken = default)
    {
        // Read the version first: a change committed while the menu is read only makes the next check reload.
        var version = await _versions.GetAsync(cancellationToken);
        if (knownVersion == version)
        {
            return new MenuDto { Version = version, NotModified = true };
        }

        var categories = await _db.Categories.AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .Select(c => new MenuCategoryDto(c.Id, c.Name, c.SortOrder))
            .ToListAsync(cancellationToken);

        var groups = await _db.ModifierGroups.AsNoTracking()
            .Where(g => g.IsActive)
            .OrderBy(g => g.Name)
            .Select(g => new MenuModifierGroupDto
            {
                Id = g.Id,
                Name = g.Name,
                MinSelections = g.MinSelections,
                MaxSelections = g.MaxSelections,
                Options = g.Options
                    .Where(o => o.IsActive)
                    .OrderBy(o => o.SortOrder)
                    .ThenBy(o => o.Name)
                    .Select(o => new MenuModifierOptionDto(o.Id, o.Name, o.PriceDelta))
                    .ToList(),
            })
            .ToListAsync(cancellationToken);
        var activeGroups = groups.Select(g => g.Id).ToHashSet();

        var items = await _db.MenuItems.AsNoTracking()
            .Where(i => i.IsActive && i.Category!.IsActive)
            .OrderBy(i => i.SortOrder)
            .ThenBy(i => i.Name)
            .Select(i => new
            {
                i.Id,
                i.CategoryId,
                i.Name,
                i.Code,
                i.Price,
                i.TaxId,
                TaxRate = i.Tax == null ? 0m : i.Tax.RatePercent,
                i.PreparationStationId,
                i.IsAvailable,
                i.ImagePath,
                i.SortOrder,
                Groups = i.ModifierGroups.OrderBy(l => l.SortOrder).Select(l => l.ModifierGroupId).ToList(),
            })
            .ToListAsync(cancellationToken);

        return new MenuDto
        {
            Version = version,
            Categories = categories,
            ModifierGroups = groups,
            Items = items.Select(i => new MenuEntryDto
            {
                Id = i.Id,
                CategoryId = i.CategoryId,
                Name = i.Name,
                Code = i.Code,
                Price = i.Price,
                TaxId = i.TaxId,
                TaxRatePercent = i.TaxRate,
                StationId = i.PreparationStationId,
                IsAvailable = i.IsAvailable,
                ImageUrl = i.ImagePath is null ? null : MenuImageRoutes.UrlFor(i.ImagePath),
                SortOrder = i.SortOrder,
                ModifierGroupIds = i.Groups.Where(activeGroups.Contains).ToList(),
            }).ToList(),
        };
    }
}
