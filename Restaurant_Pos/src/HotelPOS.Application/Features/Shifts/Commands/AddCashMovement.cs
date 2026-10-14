using System.Globalization;
using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Features.Billing;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Cash;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Cash;
using HotelPOS.Domain.Common;

namespace HotelPOS.Application.Features.Shifts.Commands;

// Pay-in, pay-out or till voucher on an open shift. Money out above CashPayOutApprovalLimit needs a manager.
public sealed record AddCashMovementCommand(int ShiftId, AddCashMovementRequest Input)
    : ICommand<ShiftReportDto>, IHasInput<AddCashMovementRequest>, IRequirePermission, IAuditedCommand, IBusinessDayGuarded
{
    public string Permission => Permissions.ShiftOperate;

    public BusinessDayScope BusinessDayScope => BusinessDayScope.Today;
}

public sealed class AddCashMovementRequestValidator : AbstractValidator<AddCashMovementRequest>
{
    public AddCashMovementRequestValidator()
    {
        RuleFor(x => x.Type).IsInEnum().WithMessage("Choose pay-in, pay-out or till voucher.");
        RuleFor(x => x.Amount).GreaterThan(0).LessThanOrEqualTo(CashLimits.MaxAmount).PrecisionScale(12, 2, true);
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Enter the reason for the cash movement.").MaximumLength(CashLimits.ReasonMaxLength);
        RuleFor(x => x.Approval!).SetValidator(new ManagerApprovalDtoValidator()).When(x => x.Approval is not null);
    }
}

internal sealed class AddCashMovementHandler : ICommandHandler<AddCashMovementCommand, ShiftReportDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly IAfterCommit _afterCommit;
    private readonly ShiftSteps _shifts;
    private readonly ShiftEvents _events;
    private readonly ManagerApprovals _approvals;

    public AddCashMovementHandler(
        IAppDbContext db,
        IAuditTrail audit,
        IAfterCommit afterCommit,
        ShiftSteps shifts,
        ShiftEvents events,
        ManagerApprovals approvals)
    {
        _db = db;
        _audit = audit;
        _afterCommit = afterCommit;
        _shifts = shifts;
        _events = events;
        _approvals = approvals;
    }

    public async Task<Result<ShiftReportDto>> Handle(AddCashMovementCommand command, CancellationToken cancellationToken)
    {
        var request = command.Input;
        var shift = await _shifts.LockAsync(command.ShiftId, cancellationToken);
        if (shift is null)
        {
            return AppErrors.NotFound("Shift", command.ShiftId);
        }

        if (_shifts.CheckAccess(shift) is { } denied)
        {
            return denied;
        }

        if (!shift.IsOpen)
        {
            return AppErrors.InvalidState($"This shift is {shift.Status}; cash movements go on an open shift.");
        }

        int? approvedBy = null;
        var limit = await _db.DecimalSettingAsync(SettingKeys.CashPayOutApprovalLimit, 1000m, cancellationToken);
        if (request.Type != CashMovementType.PayIn && request.Amount > limit)
        {
            var approval = await _approvals.ApproveAsync(request.Approval, ApprovalActions.PayOut, $"cash out of the drawer above {limit:0.00}", cancellationToken);
            if (approval.IsFailure)
            {
                return approval.Error!;
            }

            approvedBy = approval.Value;
        }

        try
        {
            var movement = shift.AddMovement(request.Type, request.Amount, request.Reason, approvedBy);
            await _db.SaveChangesAsync(cancellationToken);
            _audit.Add(AuditActions.ShiftMovementAdded, nameof(CashShift), shift.Id.ToString(CultureInfo.InvariantCulture), newValues: new
            {
                MovementId = movement.Id,
                Type = movement.Type.ToString(),
                movement.Amount,
                movement.Reason,
                ApprovedBy = approvedBy,
            });
        }
        catch (DomainException ex)
        {
            return new AppError(ex.Code, ex.Message);
        }

        _afterCommit.Enqueue(() => _events.PublishAsync(shift, ShiftChangeTypes.MovementAdded));
        return await _shifts.ReportAsync(shift, cancellationToken);
    }
}
