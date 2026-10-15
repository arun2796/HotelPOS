using System.Globalization;
using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Features.Billing;
using HotelPOS.Contracts.Cash;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Cash;
using HotelPOS.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Shifts.Commands;

// Counts the drawer and closes the shift; answers with the Z report. A variance needs a reason, and above
// CashVarianceApprovalLimit a manager's approval.
public sealed record CloseShiftCommand(int ShiftId, CloseShiftRequest Input)
    : ICommand<ShiftReportDto>, IHasInput<CloseShiftRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.ShiftOperate;
}

public sealed class CloseShiftRequestValidator : AbstractValidator<CloseShiftRequest>
{
    public CloseShiftRequestValidator()
    {
        RuleFor(x => x.RowVersion).NotEmpty();
        this.ForCounts(x => x.Counts);
        RuleFor(x => x.VarianceReason).MaximumLength(CashLimits.ReasonMaxLength);
        RuleFor(x => x.Approval!).SetValidator(new ManagerApprovalDtoValidator()).When(x => x.Approval is not null);
    }
}

internal sealed class CloseShiftHandler : ICommandHandler<CloseShiftCommand, ShiftReportDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly IAfterCommit _afterCommit;
    private readonly IClock _clock;
    private readonly ShiftSteps _shifts;
    private readonly ShiftClosing _closing;
    private readonly ShiftEvents _events;

    public CloseShiftHandler(
        IAppDbContext db,
        IAuditTrail audit,
        IAfterCommit afterCommit,
        IClock clock,
        ShiftSteps shifts,
        ShiftClosing closing,
        ShiftEvents events)
    {
        _db = db;
        _audit = audit;
        _afterCommit = afterCommit;
        _clock = clock;
        _shifts = shifts;
        _closing = closing;
        _events = events;
    }

    public async Task<Result<ShiftReportDto>> Handle(CloseShiftCommand command, CancellationToken cancellationToken)
    {
        var request = command.Input;
        var counted = await _closing.CountAsync(command.ShiftId, request.RowVersion, request.Counts, request.VarianceReason, request.Approval, cancellationToken);
        if (counted.IsFailure)
        {
            return counted.Error!;
        }

        var (shift, counts, expected, variance, approvedBy) = counted.Value;
        try
        {
            shift.Close(counts, expected, request.VarianceReason, approvedBy, _shifts.UserId, _clock.UtcNow);
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

        _audit.Add(AuditActions.ShiftClosed, nameof(CashShift), shift.Id.ToString(CultureInfo.InvariantCulture), newValues: new
        {
            shift.FloatAmount,
            Expected = expected,
            shift.CountedCash,
            Variance = variance,
            shift.VarianceReason,
            ApprovedBy = approvedBy,
        });
        _afterCommit.Enqueue(() => _events.PublishAsync(shift, ShiftChangeTypes.Closed));
        return await _shifts.ReportAsync(shift, cancellationToken);
    }
}
