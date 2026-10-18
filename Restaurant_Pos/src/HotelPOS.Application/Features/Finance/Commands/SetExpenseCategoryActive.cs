using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Finance.Commands;

public sealed record SetExpenseCategoryActiveCommand(int Id, bool IsActive)
    : ICommand, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.ExpensesManage;
}

internal sealed class SetExpenseCategoryActiveHandler : ICommandHandler<SetExpenseCategoryActiveCommand>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;

    public SetExpenseCategoryActiveHandler(IAppDbContext db, IAuditTrail audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<Result> Handle(SetExpenseCategoryActiveCommand command, CancellationToken cancellationToken)
    {
        var category = await _db.ExpenseCategories.FirstOrDefaultAsync(c => c.Id == command.Id, cancellationToken);
        if (category is null)
        {
            return AppErrors.NotFound("ExpenseCategory", command.Id);
        }

        if (command.IsActive)
        {
            category.Activate();
            _audit.Add(AuditActions.ExpenseCategoryActivated, nameof(ExpenseCategory), category.Id.ToString());
        }
        else
        {
            category.Deactivate();
            _audit.Add(AuditActions.ExpenseCategoryDeactivated, nameof(ExpenseCategory), category.Id.ToString());
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

