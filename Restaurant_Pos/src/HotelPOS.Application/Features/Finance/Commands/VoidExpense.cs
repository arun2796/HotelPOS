using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Common.Security;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Finance;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Finance.Commands;

public sealed record VoidExpenseCommand(int Id, VoidExpenseRequest Input)
    : ICommand<ExpenseDto>, IHasInput<VoidExpenseRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.ExpensesManage;
}

public sealed class VoidExpenseRequestValidator : AbstractValidator<VoidExpenseRequest>
{
    public VoidExpenseRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(ExpenseLimits.VoidReasonMaxLength);
        RuleFor(x => x.RowVersion).GreaterThan(0u);
    }
}

internal sealed class VoidExpenseHandler : ICommandHandler<VoidExpenseCommand, ExpenseDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly IBusinessDayLock _dayLock;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IAfterCommit _afterCommit;
    private readonly IRealtimeNotifier _realtime;

    public VoidExpenseHandler(
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

    public async Task<Result<ExpenseDto>> Handle(VoidExpenseCommand command, CancellationToken cancellationToken)
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
            return AppErrors.BusinessRule("Expense is already voided.");
        }

        // Financial period lock: cannot void an expense in a closed period.
        if (await _dayLock.CheckPeriodAsync(expense.ExpenseDate, cancellationToken) is { } periodClosed)
        {
            return periodClosed;
        }

        var now = _clock.UtcNow;
        var userId = _currentUser.UserId ?? 0;
        expense.Void(userId, input.Reason, now);

        await _db.SaveChangesAsync(cancellationToken);

        _audit.Add(AuditActions.ExpenseVoided, nameof(Expense), expense.Id.ToString(),
            newValues: new { expense.ExpenseDate, expense.Amount, Reason = input.Reason });

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
                    ChangeType = "Voided",
                    OccurredAtUtc = now,
                },
                RealtimeAudience.ForRoles(Roles.Admin, Roles.Manager),
                cancellationToken);
        });

        return ExpenseMapping.ToDto(expense, _currentUser.UserName);
    }
}

