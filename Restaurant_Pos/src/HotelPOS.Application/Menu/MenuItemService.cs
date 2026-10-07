using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Menu;
using HotelPOS.Domain.Menu;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Menu;

public interface IMenuItemService
{
    Task<IReadOnlyList<MenuItemDto>> ListAsync(MenuItemQuery query, CancellationToken cancellationToken = default);

    Task<Result<MenuItemDto>> GetAsync(int id, CancellationToken cancellationToken = default);

    Task<Result<MenuItemDto>> CreateAsync(CreateMenuItemRequest request, CancellationToken cancellationToken = default);

    Task<Result<MenuItemDto>> UpdateAsync(int id, UpdateMenuItemRequest request, CancellationToken cancellationToken = default);

    Task<Result<MenuItemDto>> DeactivateAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Sold-out toggle. Needs no row version from the client; a simultaneous edit still returns CONCURRENCY_CONFLICT.</summary>
    Task<Result<MenuItemDto>> SetAvailabilityAsync(int id, bool isAvailable, CancellationToken cancellationToken = default);

    /// <summary>Replaces the item picture. The content must be a JPEG or PNG of at most 2 MB.</summary>
    Task<Result<MenuItemDto>> UploadImageAsync(int id, Stream content, long length, CancellationToken cancellationToken = default);

    Task<Result<MenuItemDto>> RemoveImageAsync(int id, CancellationToken cancellationToken = default);
}

public sealed class MenuItemService : IMenuItemService
{
    private readonly IAppDbContext _db;
    private readonly IAuditService _audit;
    private readonly MenuChanges _changes;
    private readonly IMenuImageStore _images;

    public MenuItemService(IAppDbContext db, IAuditService audit, MenuChanges changes, IMenuImageStore images)
    {
        _db = db;
        _audit = audit;
        _changes = changes;
        _images = images;
    }

    public async Task<IReadOnlyList<MenuItemDto>> ListAsync(MenuItemQuery query, CancellationToken cancellationToken = default)
    {
        var items = WithReferences().AsNoTracking().Where(i => query.IncludeInactive || i.IsActive);
        if (query.CategoryId is { } categoryId)
        {
            items = items.Where(i => i.CategoryId == categoryId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToUpperInvariant();
            items = items.Where(i => i.Name.ToUpper().Contains(term) || (i.Code != null && i.Code.Contains(term)));
        }

        var list = await items
            .OrderBy(i => i.Category!.SortOrder)
            .ThenBy(i => i.SortOrder)
            .ThenBy(i => i.Name)
            .ToListAsync(cancellationToken);
        return list.Select(ToDto).ToList();
    }

    public async Task<Result<MenuItemDto>> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var item = await WithReferences().AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        return item is null ? AppErrors.NotFound("Menu item", id) : ToDto(item);
    }

    public async Task<Result<MenuItemDto>> CreateAsync(CreateMenuItemRequest request, CancellationToken cancellationToken = default)
    {
        var error = await CheckReferencesAsync(request.CategoryId, request.TaxId, request.PreparationStationId, request.ModifierGroupIds, cancellationToken)
            ?? await CheckUniqueAsync(request.CategoryId, request.Name, request.Code, null, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var item = new MenuItem(request.CategoryId, request.Name, request.Code, request.Description, request.Price,
            request.TaxId, request.PreparationStationId, request.SortOrder);
        item.SetAvailability(request.IsAvailable);
        item.SetModifierGroups(request.ModifierGroupIds);

        await using var transaction = await _changes.BeginAsync(cancellationToken);
        _db.MenuItems.Add(item);
        await _db.SaveChangesAsync(cancellationToken);
        _audit.Record(AuditActions.MenuItemCreated, nameof(MenuItem), item.Id.ToString(),
            newValues: new { item.Name, item.Code, item.CategoryId, item.Price, item.TaxId, item.PreparationStationId });
        await _changes.CommitAsync(transaction, cancellationToken);
        return await GetAsync(item.Id, cancellationToken);
    }

    public async Task<Result<MenuItemDto>> UpdateAsync(int id, UpdateMenuItemRequest request, CancellationToken cancellationToken = default)
    {
        var item = await _db.MenuItems.Include(i => i.ModifierGroups).FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null)
        {
            return AppErrors.NotFound("Menu item", id);
        }

        if (!RowVersions.Matches(item.RowVersion, request.RowVersion))
        {
            return await ConcurrencyAsync(id, cancellationToken);
        }

        var error = await CheckReferencesAsync(request.CategoryId, request.TaxId, request.PreparationStationId, request.ModifierGroupIds, cancellationToken)
            ?? await CheckUniqueAsync(request.CategoryId, request.Name, request.Code, id, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var oldPrice = item.Price;
        var oldValues = new { item.Name, item.Code, item.CategoryId, item.Price, item.TaxId, item.PreparationStationId, item.IsAvailable, item.IsActive };
        item.Update(request.CategoryId, request.Name, request.Code, request.Description, request.Price,
            request.TaxId, request.PreparationStationId, request.SortOrder);
        item.SetAvailability(request.IsAvailable);
        item.SetModifierGroups(request.ModifierGroupIds);
        if (request.IsActive)
        {
            item.Activate();
        }
        else
        {
            item.Deactivate();
        }

        // Always write the item row (even when only modifier links changed) so its row version is checked and bumped.
        _db.Entry(item).State = EntityState.Modified;
        _audit.Record(AuditActions.MenuItemUpdated, nameof(MenuItem), item.Id.ToString(), oldValues,
            new { item.Name, item.Code, item.CategoryId, item.Price, item.TaxId, item.PreparationStationId, item.IsAvailable, item.IsActive });
        if (oldPrice != item.Price)
        {
            _audit.Record(AuditActions.MenuItemPriceChanged, nameof(MenuItem), item.Id.ToString(),
                new { Price = oldPrice }, new { item.Price });
        }

        return await CommitAsync(item.Id, cancellationToken);
    }

    public async Task<Result<MenuItemDto>> DeactivateAsync(int id, CancellationToken cancellationToken = default)
    {
        var item = await _db.MenuItems.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null)
        {
            return AppErrors.NotFound("Menu item", id);
        }

        if (!item.IsActive)
        {
            return await GetAsync(id, cancellationToken);
        }

        item.Deactivate();
        _audit.Record(AuditActions.MenuItemDeactivated, nameof(MenuItem), item.Id.ToString());
        return await CommitAsync(id, cancellationToken);
    }

    public async Task<Result<MenuItemDto>> SetAvailabilityAsync(int id, bool isAvailable, CancellationToken cancellationToken = default)
    {
        var item = await _db.MenuItems.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null)
        {
            return AppErrors.NotFound("Menu item", id);
        }

        if (item.IsAvailable == isAvailable)
        {
            return await GetAsync(id, cancellationToken);
        }

        item.SetAvailability(isAvailable);
        _audit.Record(AuditActions.MenuItemAvailabilityChanged, nameof(MenuItem), item.Id.ToString(),
            new { IsAvailable = !isAvailable }, new { IsAvailable = isAvailable });
        return await CommitAsync(id, cancellationToken);
    }

    public async Task<Result<MenuItemDto>> UploadImageAsync(int id, Stream content, long length, CancellationToken cancellationToken = default)
    {
        if (length <= 0 || length > MenuLimits.MaxImageBytes)
        {
            return AppErrors.Validation("file", $"The picture must be a JPEG or PNG file of at most {MenuLimits.MaxImageBytes / (1024 * 1024)} MB.");
        }

        var item = await _db.MenuItems.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null)
        {
            return AppErrors.NotFound("Menu item", id);
        }

        var fileName = await _images.SaveAsync(id, content, cancellationToken);
        if (fileName is null)
        {
            return AppErrors.Validation("file", "The file is not a readable JPEG or PNG picture.");
        }

        var previous = item.ImagePath;
        item.SetImage(fileName);
        _audit.Record(AuditActions.MenuItemImageChanged, nameof(MenuItem), item.Id.ToString(),
            new { ImagePath = previous }, new { ImagePath = fileName });
        var result = await CommitAsync(id, cancellationToken);
        // Keep exactly one file per item: drop the replaced picture, or the new one if the save failed.
        var obsolete = result.IsSuccess ? previous : fileName;
        if (obsolete is not null)
        {
            _images.Delete(obsolete);
        }

        return result;
    }

    public async Task<Result<MenuItemDto>> RemoveImageAsync(int id, CancellationToken cancellationToken = default)
    {
        var item = await _db.MenuItems.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null)
        {
            return AppErrors.NotFound("Menu item", id);
        }

        if (item.ImagePath is not { } previous)
        {
            return await GetAsync(id, cancellationToken);
        }

        item.SetImage(null);
        _audit.Record(AuditActions.MenuItemImageChanged, nameof(MenuItem), item.Id.ToString(),
            new { ImagePath = previous }, new { ImagePath = (string?)null });
        var result = await CommitAsync(id, cancellationToken);
        if (result.IsSuccess)
        {
            _images.Delete(previous);
        }

        return result;
    }

    internal static MenuItemDto ToDto(MenuItem item) => new()
    {
        Id = item.Id,
        CategoryId = item.CategoryId,
        CategoryName = item.Category?.Name ?? string.Empty,
        Name = item.Name,
        Code = item.Code,
        Description = item.Description,
        Price = item.Price,
        TaxId = item.TaxId,
        TaxName = item.Tax?.Name,
        TaxRatePercent = item.Tax?.RatePercent ?? 0m,
        PreparationStationId = item.PreparationStationId,
        StationName = item.PreparationStation?.Name ?? string.Empty,
        IsAvailable = item.IsAvailable,
        IsActive = item.IsActive,
        ImageUrl = item.ImagePath is null ? null : MenuImageRoutes.UrlFor(item.ImagePath),
        SortOrder = item.SortOrder,
        ModifierGroupIds = item.ModifierGroups.OrderBy(l => l.SortOrder).Select(l => l.ModifierGroupId).ToList(),
        RowVersion = RowVersions.Encode(item.RowVersion),
    };

    private IQueryable<MenuItem> WithReferences() =>
        _db.MenuItems
            .Include(i => i.Category)
            .Include(i => i.Tax)
            .Include(i => i.PreparationStation)
            .Include(i => i.ModifierGroups);

    private async Task<Result<MenuItemDto>> CommitAsync(int id, CancellationToken cancellationToken)
    {
        await using var transaction = await _changes.BeginAsync(cancellationToken);
        try
        {
            await _changes.CommitAsync(transaction, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return await ConcurrencyAsync(id, cancellationToken);
        }

        return await GetAsync(id, cancellationToken);
    }

    private async Task<Result<MenuItemDto>> ConcurrencyAsync(int id, CancellationToken cancellationToken)
    {
        var current = await GetAsync(id, cancellationToken);
        return current.IsSuccess ? AppErrors.Concurrency("menu item", current.Value) : current;
    }

    private async Task<AppError?> CheckReferencesAsync(int categoryId, int? taxId, int stationId, IReadOnlyList<int> groupIds, CancellationToken cancellationToken)
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

    private async Task<AppError?> CheckUniqueAsync(int categoryId, string name, string? code, int? excludingId, CancellationToken cancellationToken)
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
