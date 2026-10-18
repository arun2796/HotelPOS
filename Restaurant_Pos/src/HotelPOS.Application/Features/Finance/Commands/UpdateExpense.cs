using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Common.Security;
using HotelPOS.Contracts.Cash;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Finance;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Finance.Commands;

public sealed record UpdateExpenseCommand(int Id, UpdateExpenseRequest Input)
    : ICommand<ExpenseDto>, IHasInput<UpdateExpenseRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.ExpensesManage;
}

public sealed class UpdateExpenseRequestValidator : AbstractValidator<UpdateExpenseRequest>
{
    public UpdateExpenseRequestValidator()
    {
        RuleFor(x => x.ExpenseDate).NotEmpty();
        RuleFor(x => x.CategoryId).GreaterThan(0);
        RuleFor(x => x.Amount).GreaterThan(0).LessThanOrEqualTo(ExpenseLimits.MaxAmount);
        RuleFor(x => x.PaymentSource).NotEmpty().Must(p => ExpensePaymentSources.All.Contains(p.Trim().ToUpperInvariant()))
            .WithMessage($"Payment source must be one of: {string.Join(", ", ExpensePaymentSources.All)}");
        RuleFor(x => x.Payee).MaximumLength(ExpenseLimits.PayeeMaxLength);
        RuleFor(x => x.Notes).MaximumLength(ExpenseLimits.NotesMaxLength);
        RuleFor(x => x.RowVersion).GreaterThan(0u);
    }
}

internal sealed class UpdateExpenseHandler : ICommandHandler<UpdateExpenseCommand, ExpenseDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly IBusinessDayLock _dayLock;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IAfterCommit _afterCommit;
    private readonly IRealtimeNotifier _realtime;

    public UpdateExpenseHandler(
        IAppDbContext db,
        IAuditTrail audit,
        IBusinessDayLock dayLock,
        ICurrentUser currentUser,
        IClock clock,
        IAfterCommit afterCommit,
        IRealtimeNotifier realtime)
    {
        _db = db;
        _audit = audit;
        _dayLock = dayLock;
        _currentUser = currentUser;
        _clock = clock;
        _afterCommit = afterCommit;
        _realtime = realtime;
    }

    public async Task<Result<ExpenseDto>> Handle(UpdateExpenseCommand command, CancellationToken cancellationToken)
    {
        var expense = await _db.Expenses
            .Include(e => e.Category)
            .Include(e => e.CashMovement)
            .FirstOrDefaultAsync(e => e.Id == command.Id, cancellationToken);

        if (expense is null)
        {
            return AppErrors.NotFound("Expense", command.Id);
        }

        var input = command.Input;
        if (expense.RowVersion != input.RowVersion)
        {
            return AppErrors.Concurrency("Expense");
        }

        if (expense.IsVoid)
        {
            return AppErrors.BusinessRule("Voided expenses cannot be updated.");
        }

        // Financial period lock: cannot edit an expense in a closed period.
        if (await _dayLock.CheckPeriodAsync(expense.ExpenseDate, cancellationToken) is { } periodClosed1)
        {
            return periodClosed1;
        }

        if (input.ExpenseDate != expense.ExpenseDate && await _dayLock.CheckPeriodAsync(input.ExpenseDate, cancellationToken) is { } periodClosed2)
        {
            return periodClosed2;
        }

        var category = await _db.ExpenseCategories.FirstOrDefaultAsync(c => c.Id == input.CategoryId, cancellationToken);
        if (category is null)
        {
            return AppErrors.NotFound("ExpenseCategory", input.CategoryId);
        }

        if (!category.IsActive && category.Id != expense.CategoryId)
        {
            return AppErrors.BusinessRule("Cannot change expense to an inactive category.");
        }

        if (input.CashMovementId is { } cmId)
        {
            var movement = await _db.CashMovements.FirstOrDefaultAsync(m => m.Id == cmId, cancellationToken);
            if (movement is null)
            {
                return AppErrors.NotFound("CashMovement", cmId);
            }

            if (movement.Type != CashMovementType.PayOut)
            {
                return AppErrors.BusinessRule("Linked cash movement must be a pay-out.");
            }

            if (await _db.Expenses.AnyAsync(e => e.Id != expense.Id && e.CashMovementId == cmId && !e.IsVoid, cancellationToken))
            {
                return AppErrors.BusinessRule("This cash pay-out is already linked to another active expense.");
            }
        }

        var now = _clock.UtcNow;
        var userId = _currentUser.UserId ?? 0;
        expense.Update(
            input.ExpenseDate,
            input.CategoryId,
            input.Amount,
            input.Payee,
            input.Notes,
            input.PaymentSource,
            input.CashMovementId,
            userId,
            now);

        await _db.SaveChangesAsync(cancellationToken);

        _audit.Add(AuditActions.ExpenseUpdated, nameof(Expense), expense.Id.ToString(),
            newValues: new
            {
                expense.ExpenseDate,
                expense.CategoryId,
                expense.Amount,
                expense.PaymentSource,
                expense.CashMovementId,
                expense.Payee,
            });

        _afterCommit.Enqueue(async () =>
        {
            await _realtime.PublishAsync(
                HubEvents.ExpenseChanged,
                new ExpenseChangedEvent
                {
                    EntityId = expense.Id,
                    ExpenseId = expense.Id,
                    ExpenseDate = expense.ExpenseDate,
                    Amount = expense.Amount,
                    ChangeType = "Updated",
                    OccurredAtUtc = now,
                },
                RealtimeAudience.ForRoles(Roles.Admin, Roles.Manager),
                cancellationToken);
        });

        var updated = await _db.Expenses
            .Include(e => e.Category)
            .Include(e => e.CashMovement)
            .FirstAsync(e => e.Id == expense.Id, cancellationToken);

        return ExpenseMapping.ToDto(updated, _currentUser.UserName);
    }
}

