using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Menu;

namespace HotelPOS.Application.Features.Menu.Commands;

public sealed record CreateCategoryCommand(CreateCategoryRequest Input)
    : ICommand<CategoryDto>, IHasInput<CreateCategoryRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.MenuManage;
}

public sealed class CreateCategoryRequestValidator : AbstractValidator<CreateCategoryRequest>
{
    public CreateCategoryRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(MenuLimits.NameMaxLength);
        RuleFor(x => x.SortOrder).ValidSortOrder();
    }
}

internal sealed class CreateCategoryHandler : ICommandHandler<CreateCategoryCommand, CategoryDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly MenuCategories _categories;
    private readonly MenuChanges _changes;

    public CreateCategoryHandler(IAppDbContext db, IAuditTrail audit, MenuCategories categories, MenuChanges changes)
    {
        _db = db;
        _audit = audit;
        _categories = categories;
        _changes = changes;
    }

    public async Task<Result<CategoryDto>> Handle(CreateCategoryCommand command, CancellationToken cancellationToken)
    {
        var request = command.Input;
        if (await _categories.NameTakenAsync(request.Name, null, cancellationToken))
        {
            return AppErrors.Duplicate($"A category named '{request.Name.Trim()}' already exists.");
        }

        var category = new Category(request.Name, request.SortOrder);
        _db.Categories.Add(category);
        await _db.SaveChangesAsync(cancellationToken);
        _audit.Add(AuditActions.CategoryCreated, nameof(Category), category.Id.ToString(),
            newValues: new { category.Name, category.SortOrder });
        await _changes.SaveAsync(cancellationToken);

        return await _categories.GetAsync(category.Id, cancellationToken);
    }
}
