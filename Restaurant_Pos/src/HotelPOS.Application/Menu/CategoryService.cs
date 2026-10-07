using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Menu;
using HotelPOS.Domain.Menu;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Menu;

public interface ICategoryService
{
    Task<IReadOnlyList<CategoryDto>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default);

    Task<Result<CategoryDto>> GetAsync(int id, CancellationToken cancellationToken = default);

    Task<Result<CategoryDto>> CreateAsync(CreateCategoryRequest request, CancellationToken cancellationToken = default);

    Task<Result<CategoryDto>> UpdateAsync(int id, UpdateCategoryRequest request, CancellationToken cancellationToken = default);

    /// <summary>Refused while the category has active items, unless <paramref name="deactivateItems"/> is true.</summary>
    Task<Result<CategoryDto>> DeactivateAsync(int id, bool deactivateItems, CancellationToken cancellationToken = default);
}

public sealed class CategoryService : ICategoryService
{
    private readonly IAppDbContext _db;
    private readonly IAuditService _audit;
    private readonly MenuChanges _changes;

    public CategoryService(IAppDbContext db, IAuditService audit, MenuChanges changes)
    {
        _db = db;
        _audit = audit;
        _changes = changes;
    }

    public async Task<IReadOnlyList<CategoryDto>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default) =>
        await Project(_db.Categories.Where(c => includeInactive || c.IsActive))
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);

    public async Task<Result<CategoryDto>> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var category = await Project(_db.Categories.Where(c => c.Id == id)).FirstOrDefaultAsync(cancellationToken);
        return category is null ? AppErrors.NotFound("Category", id) : category;
    }

    public async Task<Result<CategoryDto>> CreateAsync(CreateCategoryRequest request, CancellationToken cancellationToken = default)
    {
        if (await NameTakenAsync(request.Name, null, cancellationToken))
        {
            return AppErrors.Duplicate($"A category named '{request.Name.Trim()}' already exists.");
        }

        var category = new Category(request.Name, request.SortOrder);
        await using var transaction = await _changes.BeginAsync(cancellationToken);
        _db.Categories.Add(category);
        await _db.SaveChangesAsync(cancellationToken);
        _audit.Record(AuditActions.CategoryCreated, nameof(Category), category.Id.ToString(),
            newValues: new { category.Name, category.SortOrder });
        await _changes.CommitAsync(transaction, cancellationToken);

        return await GetAsync(category.Id, cancellationToken);
    }

    public async Task<Result<CategoryDto>> UpdateAsync(int id, UpdateCategoryRequest request, CancellationToken cancellationToken = default)
    {
        var category = await _db.Categories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (category is null)
        {
            return AppErrors.NotFound("Category", id);
        }

        if (await NameTakenAsync(request.Name, id, cancellationToken))
        {
            return AppErrors.Duplicate($"A category named '{request.Name.Trim()}' already exists.");
        }

        await using var transaction = await _changes.BeginAsync(cancellationToken);
        var oldValues = new { category.Name, category.SortOrder, category.IsActive };
        category.Update(request.Name, request.SortOrder);
        if (category.IsActive != request.IsActive)
        {
            var error = await SetActiveAsync(category, request.IsActive, request.DeactivateItems, cancellationToken);
            if (error is not null)
            {
                return error;
            }
        }

        _audit.Record(AuditActions.CategoryUpdated, nameof(Category), category.Id.ToString(),
            oldValues, new { category.Name, category.SortOrder, category.IsActive });
        await _changes.CommitAsync(transaction, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<Result<CategoryDto>> DeactivateAsync(int id, bool deactivateItems, CancellationToken cancellationToken = default)
    {
        var category = await _db.Categories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (category is null)
        {
            return AppErrors.NotFound("Category", id);
        }

        if (category.IsActive)
        {
            await using var transaction = await _changes.BeginAsync(cancellationToken);
            var error = await SetActiveAsync(category, active: false, deactivateItems, cancellationToken);
            if (error is not null)
            {
                return error;
            }

            await _changes.CommitAsync(transaction, cancellationToken);
        }

        return await GetAsync(id, cancellationToken);
    }

    private async Task<AppError?> SetActiveAsync(Category category, bool active, bool deactivateItems, CancellationToken cancellationToken)
    {
        if (active)
        {
            category.Activate();
            _audit.Record(AuditActions.CategoryActivated, nameof(Category), category.Id.ToString());
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
            _audit.Record(AuditActions.MenuItemDeactivated, nameof(MenuItem), item.Id.ToString(), newValues: new { Reason = "Category deactivated" });
        }

        category.Deactivate();
        _audit.Record(AuditActions.CategoryDeactivated, nameof(Category), category.Id.ToString(),
            newValues: new { ItemsDeactivated = activeItems.Count });
        return null;
    }

    private Task<bool> NameTakenAsync(string name, int? excludingId, CancellationToken cancellationToken)
    {
        var normalized = name.Trim().ToUpperInvariant();
        return _db.Categories.AnyAsync(c => c.Name.ToUpper() == normalized && c.Id != excludingId, cancellationToken);
    }

    private IQueryable<CategoryDto> Project(IQueryable<Category> categories) =>
        categories.AsNoTracking().Select(c => new CategoryDto
        {
            Id = c.Id,
            Name = c.Name,
            SortOrder = c.SortOrder,
            IsActive = c.IsActive,
            ItemCount = _db.MenuItems.Count(i => i.CategoryId == c.Id && i.IsActive),
        });
}
