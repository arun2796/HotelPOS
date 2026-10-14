using System.Globalization;
using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Cash;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Cash;
using HotelPOS.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Shifts.Commands;

// A manager counts and closes the drawer of a cashier who is not there; the reason covers the close and any variance.
public sealed record ForceCloseShiftCommand(int ShiftId, ForceCloseShiftRequest Input)
    : ICommand<ShiftReportDto>, IHasInput<ForceCloseShiftRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.ShiftManage;
}

public sealed class ForceCloseShiftRequestValidator : AbstractValidator<ForceCloseShiftRequest>
{
    public ForceCloseShiftRequestValidator()
    {
        RuleFor(x => x.RowVersion).NotEmpty();
        this.ForCounts(x => x.Counts);
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Enter the reason for the force close.").MaximumLength(CashLimits.ReasonMaxLength);
    }
}

internal sealed class ForceCloseShiftHandler : ICommandHandler<ForceCloseShiftCommand, ShiftReportDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly IAfterCommit _afterCommit;
    private readonly IClock _clock;
    private readonly ShiftSteps _shifts;
    private readonly ShiftEvents _events;

    public ForceCloseShiftHandler(IAppDbContext db, IAuditTrail audit, IAfterCommit afterCommit, IClock clock, ShiftSteps shifts, ShiftEvents events)
    {
        _db = db;
        _audit = audit;
        _afterCommit = afterCommit;
        _clock = clock;
        _shifts = shifts;
        _events = events;
    }

    public async Task<Result<ShiftReportDto>> Handle(ForceCloseShiftCommand command, CancellationToken cancellationToken)
    {
        var request = command.Input;
        var shift = await _shifts.LockAsync(command.ShiftId, cancellationToken);
        if (shift is null)
        {
            return AppErrors.NotFound("Shift", command.ShiftId);
        }

        if (!shift.IsOpen)
        {
            return AppErrors.InvalidState($"This shift is already {shift.Status}.", await _shifts.ToDtoAsync(shift, cancellationToken));
        }

        if (!RowVersions.Matches(shift.RowVersion, request.RowVersion))
        {
            return AppErrors.Concurrency("shift", await _shifts.ToDtoAsync(shift, cancellationToken));
        }

        var counts = await _shifts.CountsAsync(request.Counts, cancellationToken);
        if (counts.IsFailure)
        {
            return counts.Error!;
        }

        var expected = (await _shifts.BreakdownAsync(shift, cancellationToken)).Expected;
        try
        {
            shift.ForceClose(counts.Value, expected, request.Reason, _shifts.UserId, _clock.UtcNow);
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

        _audit.Add(AuditActions.ShiftForceClosed, nameof(CashShift), shift.Id.ToString(CultureInfo.InvariantCulture), newValues: new
        {
            shift.CashierUserId,
            Expected = expected,
            shift.CountedCash,
            shift.Variance,
            shift.ForcedReason,
        });
        _afterCommit.Enqueue(() => _events.PublishAsync(shift, ShiftChangeTypes.ForceClosed));
        return await _shifts.ReportAsync(shift, cancellationToken);
    }
}
