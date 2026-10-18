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

public sealed record CreateExpenseCommand(CreateExpenseRequest Input)
    : ICommand<ExpenseDto>, IHasInput<CreateExpenseRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.ExpensesManage;
}

public sealed class CreateExpenseRequestValidator : AbstractValidator<CreateExpenseRequest>
{
    public CreateExpenseRequestValidator()
    {
        RuleFor(x => x.ExpenseDate).NotEmpty();
        RuleFor(x => x.CategoryId).GreaterThan(0);
        RuleFor(x => x.Amount).GreaterThan(0).LessThanOrEqualTo(ExpenseLimits.MaxAmount);
        RuleFor(x => x.PaymentSource).NotEmpty().Must(p => ExpensePaymentSources.All.Contains(p.Trim().ToUpperInvariant()))
            .WithMessage($"Payment source must be one of: {string.Join(", ", ExpensePaymentSources.All)}");
        RuleFor(x => x.Payee).MaximumLength(ExpenseLimits.PayeeMaxLength);
        RuleFor(x => x.Notes).MaximumLength(ExpenseLimits.NotesMaxLength);
    }
}

internal sealed class CreateExpenseHandler : ICommandHandler<CreateExpenseCommand, ExpenseDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly IBusinessDayLock _dayLock;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IAfterCommit _afterCommit;
    private readonly IRealtimeNotifier _realtime;

    public CreateExpenseHandler(
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

    public async Task<Result<ExpenseDto>> Handle(CreateExpenseCommand command, CancellationToken cancellationToken)
    {
        var input = command.Input;

        // Financial period lock: closed periods cannot accept new expenses.
        if (await _dayLock.CheckPeriodAsync(input.ExpenseDate, cancellationToken) is { } periodClosed)
        {
            return periodClosed;
        }

        var category = await _db.ExpenseCategories.FirstOrDefaultAsync(c => c.Id == input.CategoryId, cancellationToken);
        if (category is null)
        {
            return AppErrors.NotFound("ExpenseCategory", input.CategoryId);
        }

        if (!category.IsActive)
        {
            return AppErrors.BusinessRule("Category is inactive and cannot be used for new expenses.");
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

            if (await _db.Expenses.AnyAsync(e => e.CashMovementId == cmId && !e.IsVoid, cancellationToken))
            {
                return AppErrors.BusinessRule("This cash pay-out is already linked to an active expense.");
            }
        }

        var now = _clock.UtcNow;
        var userId = _currentUser.UserId ?? 0;
        var expense = new Expense(
            input.ExpenseDate,
            input.CategoryId,
            input.Amount,
            input.Payee,
            input.Notes,
            input.PaymentSource,
            input.CashMovementId,
            userId,
            now);

        _db.Expenses.Add(expense);
        await _db.SaveChangesAsync(cancellationToken);

        _audit.Add(AuditActions.ExpenseCreated, nameof(Expense), expense.Id.ToString(),
            newValues: new
            {
                expense.ExpenseDate,
                expense.CategoryId,
                category.Name,
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
                    ChangeType = "Created",
                    OccurredAtUtc = now,
                },
                RealtimeAudience.ForRoles(Roles.Admin, Roles.Manager),
                cancellationToken);
        });

        var created = await _db.Expenses
            .Include(e => e.Category)
            .Include(e => e.CashMovement)
            .FirstAsync(e => e.Id == expense.Id, cancellationToken);

        return ExpenseMapping.ToDto(created, _currentUser.UserName);
    }
}

