using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Menu.Commands;

public sealed record DeactivateCategoryCommand(int Id, bool DeactivateItems) : ICommand<CategoryDto>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.MenuManage;
}

internal sealed class DeactivateCategoryHandler : ICommandHandler<DeactivateCategoryCommand, CategoryDto>
{
    private readonly IAppDbContext _db;
    private readonly MenuCategories _categories;
    private readonly MenuChanges _changes;

    public DeactivateCategoryHandler(IAppDbContext db, MenuCategories categories, MenuChanges changes)
    {
        _db = db;
        _categories = categories;
        _changes = changes;
    }

    public async Task<Result<CategoryDto>> Handle(DeactivateCategoryCommand command, CancellationToken cancellationToken)
    {
        var category = await _db.Categories.FirstOrDefaultAsync(c => c.Id == command.Id, cancellationToken);
        if (category is null)
        {
            return AppErrors.NotFound("Category", command.Id);
        }

        if (category.IsActive)
        {
            var error = await _categories.SetActiveAsync(category, active: false, command.DeactivateItems, cancellationToken);
            if (error is not null)
            {
                return error;
            }

            await _changes.SaveAsync(cancellationToken);
        }

        return await _categories.GetAsync(command.Id, cancellationToken);
    }
}
