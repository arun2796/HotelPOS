using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Menu;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Menu.Commands;

public sealed record UpdateCategoryCommand(int Id, UpdateCategoryRequest Input)
    : ICommand<CategoryDto>, IHasInput<UpdateCategoryRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.MenuManage;
}

public sealed class UpdateCategoryRequestValidator : AbstractValidator<UpdateCategoryRequest>
{
    public UpdateCategoryRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(MenuLimits.NameMaxLength);
        RuleFor(x => x.SortOrder).ValidSortOrder();
    }
}

internal sealed class UpdateCategoryHandler : ICommandHandler<UpdateCategoryCommand, CategoryDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly MenuCategories _categories;
    private readonly MenuChanges _changes;

    public UpdateCategoryHandler(IAppDbContext db, IAuditTrail audit, MenuCategories categories, MenuChanges changes)
    {
        _db = db;
        _audit = audit;
        _categories = categories;
        _changes = changes;
    }

    public async Task<Result<CategoryDto>> Handle(UpdateCategoryCommand command, CancellationToken cancellationToken)
    {
        var (id, request) = (command.Id, command.Input);
        var category = await _db.Categories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (category is null)
        {
            return AppErrors.NotFound("Category", id);
        }

        if (await _categories.NameTakenAsync(request.Name, id, cancellationToken))
        {
            return AppErrors.Duplicate($"A category named '{request.Name.Trim()}' already exists.");
        }

        var oldValues = new { category.Name, category.SortOrder, category.IsActive };
        category.Update(request.Name, request.SortOrder);
        if (category.IsActive != request.IsActive)
        {
            var error = await _categories.SetActiveAsync(category, request.IsActive, request.DeactivateItems, cancellationToken);
            if (error is not null)
            {
                return error;
            }
        }

        _audit.Add(AuditActions.CategoryUpdated, nameof(Category), category.Id.ToString(),
            oldValues, new { category.Name, category.SortOrder, category.IsActive });
        await _changes.SaveAsync(cancellationToken);
        return await _categories.GetAsync(id, cancellationToken);
    }
}
