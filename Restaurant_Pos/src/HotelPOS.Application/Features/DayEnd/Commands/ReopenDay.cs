using System.Globalization;
using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Features.Billing;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Cash;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.DayEnd.Commands;

// A manager reopens a closed day (a late bill, a miscounted shift) with a reason; Day End can run again afterwards.
public sealed record ReopenDayCommand(DateOnly Day, ReopenDayRequest Input)
    : ICommand<BusinessDayCloseDto>, IHasInput<ReopenDayRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.DayEndRun;
}

public sealed class ReopenDayRequestValidator : AbstractValidator<ReopenDayRequest>
{
    public ReopenDayRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Enter the reason for reopening the day.").MaximumLength(CashLimits.ReasonMaxLength);
        RuleFor(x => x.Approval!).SetValidator(new ManagerApprovalDtoValidator()).When(x => x.Approval is not null);
    }
}

internal sealed class ReopenDayHandler : ICommandHandler<ReopenDayCommand, BusinessDayCloseDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly IAfterCommit _afterCommit;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IBusinessDayLock _days;
    private readonly ManagerApprovals _approvals;
    private readonly DayEndRecords _records;

    public ReopenDayHandler(
        IAppDbContext db,
        IAuditTrail audit,
        IAfterCommit afterCommit,
        ICurrentUser currentUser,
        IClock clock,
        IBusinessDayLock days,
        ManagerApprovals approvals,
        DayEndRecords records)
    {
        _db = db;
        _audit = audit;
        _afterCommit = afterCommit;
        _currentUser = currentUser;
        _clock = clock;
        _days = days;
        _approvals = approvals;
        _records = records;
    }

    public async Task<Result<BusinessDayCloseDto>> Handle(ReopenDayCommand command, CancellationToken cancellationToken)
    {
        await _days.LockExclusiveAsync(command.Day, cancellationToken);
        var close = await _db.BusinessDayCloses.FirstOrDefaultAsync(c => c.BusinessDay == command.Day, cancellationToken);
        if (close is null || !close.IsClosed)
        {
            return AppErrors.InvalidState($"Business day {command.Day:dd-MM-yyyy} is not closed.");
        }

        var approval = await _approvals.ApproveAsync(command.Input.Approval, ApprovalActions.ReopenDay, "reopening a closed day", cancellationToken);
        if (approval.IsFailure)
        {
            return approval.Error!;
        }

        try
        {
            close.Reopen(_currentUser.UserId ?? approval.Value, command.Input.Reason, _clock.UtcNow);
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DomainException ex)
        {
            return new AppError(ex.Code, ex.Message);
        }

        _audit.Add(AuditActions.DayEndReopened, "BusinessDay", command.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            oldValues: new { Status = "Closed", close.BackupFile },
            newValues: new { close.ReopenReason, ApprovedBy = approval.Value });
        var by = _currentUser.UserName ?? string.Empty;
        _afterCommit.Enqueue(() => _records.PublishAsync(HubEvents.BusinessDayReopened, close, by));
        return (await _records.ToDtosAsync(new[] { close }, cancellationToken))[0];
    }
}
