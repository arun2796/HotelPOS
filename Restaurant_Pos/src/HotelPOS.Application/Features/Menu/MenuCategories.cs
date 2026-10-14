using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Menu;
using HotelPOS.Domain.Menu;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Menu;

// Steps shared by the category commands and queries.
internal sealed class MenuCategories
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;

    public MenuCategories(IAppDbContext db, IAuditTrail audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<Result<CategoryDto>> GetAsync(int id, CancellationToken cancellationToken)
    {
        var category = await Project(_db.Categories.Where(c => c.Id == id)).FirstOrDefaultAsync(cancellationToken);
        return category is null ? AppErrors.NotFound("Category", id) : category;
    }

    public IQueryable<CategoryDto> Project(IQueryable<Category> categories) =>
        categories.AsNoTracking().Select(c => new CategoryDto
        {
            Id = c.Id,
            Name = c.Name,
            SortOrder = c.SortOrder,
            IsActive = c.IsActive,
            ItemCount = _db.MenuItems.Count(i => i.CategoryId == c.Id && i.IsActive),
        });

    public Task<bool> NameTakenAsync(string name, int? excludingId, CancellationToken cancellationToken)
    {
        var normalized = name.Trim().ToUpperInvariant();
        return _db.Categories.AnyAsync(c => c.Name.ToUpper() == normalized && c.Id != excludingId, cancellationToken);
    }

    public async Task<AppError?> SetActiveAsync(Category category, bool active, bool deactivateItems, CancellationToken cancellationToken)
    {
        if (active)
        {
            category.Activate();
            _audit.Add(AuditActions.CategoryActivated, nameof(Category), category.Id.ToString());
            return null;
        }

        var activeItems = await _db.MenuItems.Where(i => i.CategoryId == category.Id && i.IsActive).ToListAsync(cancellationToken);
        if (activeItems.Count > 0 && !deactivateItems)
        {
            return AppErrors.BusinessRule(
                $"Category '{category.Name}' still has {activeItems.Count} active item(s). Deactivate them first, or confirm deactivating them too.");
        }

        foreach (var item in activeItems)
        {
            item.Deactivate();
            _audit.Add(AuditActions.MenuItemDeactivated, nameof(MenuItem), item.Id.ToString(), newValues: new { Reason = "Category deactivated" });
        }

        category.Deactivate();
        _audit.Add(AuditActions.CategoryDeactivated, nameof(Category), category.Id.ToString(),
            newValues: new { ItemsDeactivated = activeItems.Count });
        return null;
    }
}
