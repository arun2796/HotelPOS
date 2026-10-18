using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Finance;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Finance.Commands;

public sealed record UpdateExpenseCategoryCommand(int Id, UpdateExpenseCategoryRequest Input)
    : ICommand<ExpenseCategoryDto>, IHasInput<UpdateExpenseCategoryRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.ExpensesManage;
}

public sealed class UpdateExpenseCategoryRequestValidator : AbstractValidator<UpdateExpenseCategoryRequest>
{
    public UpdateExpenseCategoryRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(ExpenseLimits.CategoryNameMaxLength);
        RuleFor(x => x.Description).MaximumLength(ExpenseLimits.CategoryDescriptionMaxLength);
    }
}

internal sealed class UpdateExpenseCategoryHandler : ICommandHandler<UpdateExpenseCategoryCommand, ExpenseCategoryDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;

    public UpdateExpenseCategoryHandler(IAppDbContext db, IAuditTrail audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<Result<ExpenseCategoryDto>> Handle(UpdateExpenseCategoryCommand command, CancellationToken cancellationToken)
    {
        var category = await _db.ExpenseCategories.FirstOrDefaultAsync(c => c.Id == command.Id, cancellationToken);
        if (category is null)
        {
            return AppErrors.NotFound("ExpenseCategory", command.Id);
        }

        var input = command.Input;
        var cleanName = input.Name.Trim();
        if (await _db.ExpenseCategories.AnyAsync(c => c.Id != command.Id && c.Name.ToLower() == cleanName.ToLower(), cancellationToken))
        {
            return AppErrors.Duplicate($"An expense category named '{cleanName}' already exists.");
        }

        category.Update(cleanName, input.Description, input.SortOrder);
        await _db.SaveChangesAsync(cancellationToken);

        _audit.Add(AuditActions.ExpenseCategoryUpdated, nameof(ExpenseCategory), category.Id.ToString(),
            newValues: new { category.Name, category.SortOrder, category.Description });

        return ExpenseMapping.ToDto(category);
    }
}

