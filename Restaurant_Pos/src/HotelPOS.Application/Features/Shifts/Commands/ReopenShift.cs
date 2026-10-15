using System.Globalization;
using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Features.Billing;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Cash;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Cash;
using HotelPOS.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Shifts.Commands;

// Reopens a closed or force-closed shift (a miscount found later); it is counted again at its next close. Its business
// day must still be open, so a closed day's figures never change underneath its Day End snapshot.
public sealed record ReopenShiftCommand(int ShiftId, ReopenShiftRequest Input)
    : ICommand<CashShiftDto>, IHasInput<ReopenShiftRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.ShiftManage;
}

public sealed class ReopenShiftRequestValidator : AbstractValidator<ReopenShiftRequest>
{
    public ReopenShiftRequestValidator()
    {
        RuleFor(x => x.RowVersion).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Enter the reason for reopening the shift.").MaximumLength(CashLimits.ReasonMaxLength);
        RuleFor(x => x.Approval!).SetValidator(new ManagerApprovalDtoValidator()).When(x => x.Approval is not null);
    }
}

internal sealed class ReopenShiftHandler : ICommandHandler<ReopenShiftCommand, CashShiftDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly IAfterCommit _afterCommit;
    private readonly IBusinessDayLock _days;
    private readonly ShiftSteps _shifts;
    private readonly ShiftEvents _events;
    private readonly ManagerApprovals _approvals;

    public ReopenShiftHandler(
        IAppDbContext db,
        IAuditTrail audit,
        IAfterCommit afterCommit,
        IBusinessDayLock days,
        ShiftSteps shifts,
        ShiftEvents events,
        ManagerApprovals approvals)
    {
        _db = db;
        _audit = audit;
        _afterCommit = afterCommit;
        _days = days;
        _shifts = shifts;
        _events = events;
        _approvals = approvals;
    }

    public async Task<Result<CashShiftDto>> Handle(ReopenShiftCommand command, CancellationToken cancellationToken)
    {
        var request = command.Input;
        var shift = await _shifts.LockAsync(command.ShiftId, cancellationToken);
        if (shift is null)
        {
            return AppErrors.NotFound("Shift", command.ShiftId);
        }

        if (!RowVersions.Matches(shift.RowVersion, request.RowVersion))
        {
            return AppErrors.Concurrency("shift", await _shifts.ToDtoAsync(shift, cancellationToken));
        }

        if (await _days.EnterAsync(new BusinessDayScope(Day: shift.BusinessDay), cancellationToken) is { } closed)
        {
            return closed;
        }

        if (await _db.CashShifts.AnyAsync(s => s.Id != shift.Id && s.Status == CashShiftStatus.Open
            && (s.DeviceId == shift.DeviceId || s.CashierUserId == shift.CashierUserId), cancellationToken))
        {
            return AppErrors.ShiftAlreadyOpen("Another shift is open on that terminal or for that cashier. Close it before reopening this one.");
        }

        var approval = await _approvals.ApproveAsync(request.Approval, ApprovalActions.ReopenShift, "reopening a shift", cancellationToken);
        if (approval.IsFailure)
        {
            return approval.Error!;
        }

        var before = new { shift.Status, shift.ExpectedCash, shift.CountedCash, shift.Variance, shift.VarianceReason };
        try
        {
            shift.Reopen();
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DomainException ex)
        {
            return new AppError(ex.Code, ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            return AppErrors.Concurrency("shift");
        }

        _audit.Add(AuditActions.ShiftReopened, nameof(CashShift), shift.Id.ToString(CultureInfo.InvariantCulture), before,
            new { Reason = request.Reason.Trim(), ApprovedBy = approval.Value });
        _afterCommit.Enqueue(() => _events.PublishAsync(shift, ShiftChangeTypes.Reopened));
        return await _shifts.ToDtoAsync(shift, cancellationToken);
    }
}
