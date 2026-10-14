using System.Globalization;
using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Cash;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Cash;
using HotelPOS.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Shifts.Commands;

// Opens a shift on the caller's terminal for the caller, with the counted float.
public sealed record OpenShiftCommand(OpenShiftRequest Input)
    : ICommand<CashShiftDto>, IHasInput<OpenShiftRequest>, IRequirePermission, IAuditedCommand, IBusinessDayGuarded
{
    public string Permission => Permissions.ShiftOperate;

    public BusinessDayScope BusinessDayScope => BusinessDayScope.Today;
}

public sealed class OpenShiftRequestValidator : AbstractValidator<OpenShiftRequest>
{
    public OpenShiftRequestValidator()
    {
        this.ForCounts(x => x.Counts);
    }
}

internal sealed class OpenShiftHandler : ICommandHandler<OpenShiftCommand, CashShiftDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly IAfterCommit _afterCommit;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IBusinessDayLock _days;
    private readonly ShiftSteps _shifts;
    private readonly ShiftEvents _events;

    public OpenShiftHandler(
        IAppDbContext db,
        IAuditTrail audit,
        IAfterCommit afterCommit,
        ICurrentUser currentUser,
        IClock clock,
        IBusinessDayLock days,
        ShiftSteps shifts,
        ShiftEvents events)
    {
        _db = db;
        _audit = audit;
        _afterCommit = afterCommit;
        _currentUser = currentUser;
        _clock = clock;
        _days = days;
        _shifts = shifts;
        _events = events;
    }

    public async Task<Result<CashShiftDto>> Handle(OpenShiftCommand command, CancellationToken cancellationToken)
    {
        if (_currentUser.DeviceId is not { } deviceId)
        {
            return AppErrors.BusinessRule("A shift is opened on a billing terminal. Sign in on the terminal to open one.");
        }

        var counts = await _shifts.CountsAsync(command.Input.Counts, cancellationToken);
        if (counts.IsFailure)
        {
            return counts.Error!;
        }

        if (await OpenShiftErrorAsync(deviceId, _shifts.UserId, cancellationToken) is { } open)
        {
            return open;
        }

        var shift = new CashShift(
            deviceId,
            await _days.TodayAsync(cancellationToken),
            _shifts.UserId,
            counts.Value.ToList(),
            await _db.BoolSettingAsync(SettingKeys.CashBlindCount, false, cancellationToken),
            _shifts.UserId,
            _clock.UtcNow);
        _db.CashShifts.Add(shift);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two opens raced past the check; the unique index on open shifts let only one through.
            _db.ChangeTracker.Clear();
            return await OpenShiftErrorAsync(deviceId, _shifts.UserId, cancellationToken)
                ?? AppErrors.ShiftAlreadyOpen("A shift was opened on this terminal at the same moment. Reload the shift bar.");
        }

        _audit.Add(AuditActions.ShiftOpened, nameof(CashShift), shift.Id.ToString(CultureInfo.InvariantCulture), newValues: new
        {
            shift.DeviceId,
            shift.BusinessDay,
            shift.CashierUserId,
            shift.FloatAmount,
            shift.BlindCount,
        });
        _afterCommit.Enqueue(() => _events.PublishAsync(shift, ShiftChangeTypes.Opened));
        return await _shifts.ToDtoAsync(shift, cancellationToken);
    }

    private async Task<AppError?> OpenShiftErrorAsync(Guid deviceId, int userId, CancellationToken cancellationToken)
    {
        var onDevice = await _db.CashShifts.Include(s => s.Counts).Include(s => s.Movements)
            .FirstOrDefaultAsync(s => s.DeviceId == deviceId && s.Status == CashShiftStatus.Open, cancellationToken);
        if (onDevice is not null)
        {
            return AppErrors.ShiftAlreadyOpen("A shift is already open on this terminal.", await _shifts.ToDtoAsync(onDevice, cancellationToken));
        }

        var elsewhere = await _shifts.OpenShiftDeviceOfAsync(userId, cancellationToken);
        return elsewhere is null ? null : AppErrors.ShiftAlreadyOpen($"You already hold an open shift on {elsewhere}. Close or hand it over first.");
    }
}
