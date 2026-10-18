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

public sealed record CreateExpenseCategoryCommand(CreateExpenseCategoryRequest Input)
    : ICommand<ExpenseCategoryDto>, IHasInput<CreateExpenseCategoryRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.ExpensesManage;
}

public sealed class CreateExpenseCategoryRequestValidator : AbstractValidator<CreateExpenseCategoryRequest>
{
    public CreateExpenseCategoryRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(ExpenseLimits.CategoryNameMaxLength);
        RuleFor(x => x.Description).MaximumLength(ExpenseLimits.CategoryDescriptionMaxLength);
    }
}

internal sealed class CreateExpenseCategoryHandler : ICommandHandler<CreateExpenseCategoryCommand, ExpenseCategoryDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;

    public CreateExpenseCategoryHandler(IAppDbContext db, IAuditTrail audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<Result<ExpenseCategoryDto>> Handle(CreateExpenseCategoryCommand command, CancellationToken cancellationToken)
    {
        var input = command.Input;
        var cleanName = input.Name.Trim();
        if (await _db.ExpenseCategories.AnyAsync(c => c.Name.ToLower() == cleanName.ToLower(), cancellationToken))
        {
            return AppErrors.Duplicate($"An expense category named '{cleanName}' already exists.");
        }

        var category = new ExpenseCategory(cleanName, input.Description, input.SortOrder);
        _db.ExpenseCategories.Add(category);
        await _db.SaveChangesAsync(cancellationToken);

        _audit.Add(AuditActions.ExpenseCategoryCreated, nameof(ExpenseCategory), category.Id.ToString(),
            newValues: new { category.Name, category.SortOrder, category.Description });

        return ExpenseMapping.ToDto(category);
    }
}

