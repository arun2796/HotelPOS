using System.Globalization;
using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Features.Billing;
using HotelPOS.Application.Features.Security;
using HotelPOS.Contracts.Cash;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Cash;
using HotelPOS.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Shifts.Commands;

// Closes one cashier's shift and opens the next cashier's on the same terminal in one step; the counted cash becomes
// the new float, note for note.
public sealed record HandOverShiftCommand(int ShiftId, HandOverShiftRequest Input)
    : ICommand<HandOverResultDto>, IHasInput<HandOverShiftRequest>, IRequirePermission, IAuditedCommand, IBusinessDayGuarded
{
    public string Permission => Permissions.ShiftOperate;

    public BusinessDayScope BusinessDayScope => BusinessDayScope.Today;
}

public sealed class HandOverShiftRequestValidator : AbstractValidator<HandOverShiftRequest>
{
    public HandOverShiftRequestValidator()
    {
        RuleFor(x => x.RowVersion).NotEmpty();
        RuleFor(x => x.NextCashierUserId).GreaterThan(0).WithMessage("Choose the cashier who takes over.");
        this.ForCounts(x => x.Counts);
        RuleFor(x => x.VarianceReason).MaximumLength(CashLimits.ReasonMaxLength);
        RuleFor(x => x.Approval!).SetValidator(new ManagerApprovalDtoValidator()).When(x => x.Approval is not null);
    }
}

internal sealed class HandOverShiftHandler : ICommandHandler<HandOverShiftCommand, HandOverResultDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly IAfterCommit _afterCommit;
    private readonly IClock _clock;
    private readonly IBusinessDayLock _days;
    private readonly RolePermissions _permissions;
    private readonly ShiftSteps _shifts;
    private readonly ShiftClosing _closing;
    private readonly ShiftEvents _events;

    public HandOverShiftHandler(
        IAppDbContext db,
        IAuditTrail audit,
        IAfterCommit afterCommit,
        IClock clock,
        IBusinessDayLock days,
        RolePermissions permissions,
        ShiftSteps shifts,
        ShiftClosing closing,
        ShiftEvents events)
    {
        _db = db;
        _audit = audit;
        _afterCommit = afterCommit;
        _clock = clock;
        _days = days;
        _permissions = permissions;
        _shifts = shifts;
        _closing = closing;
        _events = events;
    }

    public async Task<Result<HandOverResultDto>> Handle(HandOverShiftCommand command, CancellationToken cancellationToken)
    {
        var request = command.Input;
        var counted = await _closing.CountAsync(command.ShiftId, request.RowVersion, request.Counts, request.VarianceReason, request.Approval, cancellationToken);
        if (counted.IsFailure)
        {
            return counted.Error!;
        }

        var (shift, counts, expected, variance, approvedBy) = counted.Value;
        if (await NextCashierErrorAsync(shift, request.NextCashierUserId, cancellationToken) is { } refused)
        {
            return refused;
        }

        var now = _clock.UtcNow;
        CashShift next;
        try
        {
            // Two saves: the old shift must leave Open before the new one may be open on the same terminal.
            shift.HandOver(counts, expected, request.VarianceReason, approvedBy, _shifts.UserId, now);
            await _db.SaveChangesAsync(cancellationToken);
            next = new CashShift(shift.DeviceId, await _days.TodayAsync(cancellationToken), request.NextCashierUserId, counts, shift.BlindCount, _shifts.UserId, now);
            _db.CashShifts.Add(next);
            await _db.SaveChangesAsync(cancellationToken);
            shift.LinkSuccessor(next.Id);
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
        catch (DbUpdateException)
        {
            return AppErrors.ShiftAlreadyOpen("The next cashier opened a shift elsewhere at the same moment. Try again.");
        }

        _audit.Add(AuditActions.ShiftHandedOver, nameof(CashShift), shift.Id.ToString(CultureInfo.InvariantCulture), newValues: new
        {
            Expected = expected,
            shift.CountedCash,
            Variance = variance,
            shift.VarianceReason,
            ApprovedBy = approvedBy,
            NextShiftId = next.Id,
            NextCashierUserId = next.CashierUserId,
        });
        _audit.Add(AuditActions.ShiftOpened, nameof(CashShift), next.Id.ToString(CultureInfo.InvariantCulture), newValues: new
        {
            next.DeviceId,
            next.BusinessDay,
            next.CashierUserId,
            next.FloatAmount,
            HandedOverFromShiftId = shift.Id,
        });
        _afterCommit.Enqueue(() => _events.PublishAsync(shift, ShiftChangeTypes.HandedOver));
        _afterCommit.Enqueue(() => _events.PublishAsync(next, ShiftChangeTypes.Opened));
        return new HandOverResultDto
        {
            Closed = await _shifts.ReportAsync(shift, cancellationToken),
            Next = await _shifts.ToDtoAsync(next, cancellationToken),
        };
    }

    private async Task<AppError?> NextCashierErrorAsync(CashShift shift, int nextUserId, CancellationToken cancellationToken)
    {
        if (nextUserId == shift.CashierUserId)
        {
            return AppErrors.Validation("nextCashierUserId", "Hand over to another cashier; to keep working, simply keep the shift open.");
        }

        var next = await _db.Users.AsNoTracking().Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == nextUserId, cancellationToken);
        if (next is null || !next.IsActive)
        {
            return AppErrors.Validation("nextCashierUserId", "Choose an active cashier to take over.");
        }

        var granted = await _permissions.ForRolesAsync(next.RoleNames, cancellationToken);
        if (!next.HasRole(Roles.Admin) && !granted.Contains(Permissions.ShiftOperate, StringComparer.Ordinal))
        {
            return AppErrors.Validation("nextCashierUserId", $"{next.DisplayName} may not operate a cash shift.");
        }

        var elsewhere = await _shifts.OpenShiftDeviceOfAsync(nextUserId, cancellationToken);
        return elsewhere is null ? null : AppErrors.ShiftAlreadyOpen($"{next.DisplayName} already holds an open shift on {elsewhere}.");
    }
}
