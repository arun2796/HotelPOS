using System.Globalization;
using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Features.Shifts;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Billing;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Billing.Commands;

// Refunds (part of) a payment of a settled bill. Cashiers may send it too: it then needs a manager's approval.
public sealed record RefundPaymentCommand(int Id, RefundRequest Input, Guid IdempotencyKey)
    : ICommand<BillDetailDto>, IHasInput<RefundRequest>, IRequirePermission, IAuditedCommand, IBusinessDayGuarded
{
    public BusinessDayScope BusinessDayScope => new(Id);

    public string Permission => Permissions.BillingOperate;
}

public sealed class RefundRequestValidator : AbstractValidator<RefundRequest>
{
    public RefundRequestValidator()
    {
        RuleFor(x => x.RowVersion).NotEmpty();
        RuleFor(x => x.PaymentId).GreaterThan(0);
        RuleFor(x => x.Amount).GreaterThan(0).LessThanOrEqualTo(BillingLimits.MaxAmount).PrecisionScale(18, 2, true);
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Enter the reason for the refund.").MaximumLength(BillingLimits.ReasonMaxLength);
        RuleFor(x => x.Approval!).SetValidator(new ManagerApprovalDtoValidator()).When(x => x.Approval is not null);
    }
}

internal sealed class RefundPaymentHandler : ICommandHandler<RefundPaymentCommand, BillDetailDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly IAfterCommit _afterCommit;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly BillWorkflow _workflow;
    private readonly BillSteps _bills;
    private readonly BillEvents _events;
    private readonly ManagerApprovals _approvals;
    private readonly ShiftSteps _shifts;

    public RefundPaymentHandler(
        IAppDbContext db,
        IAuditTrail audit,
        IAfterCommit afterCommit,
        ICurrentUser currentUser,
        IClock clock,
        BillWorkflow workflow,
        BillSteps bills,
        BillEvents events,
        ManagerApprovals approvals,
        ShiftSteps shifts)
    {
        _shifts = shifts;
        _db = db;
        _audit = audit;
        _afterCommit = afterCommit;
        _currentUser = currentUser;
        _clock = clock;
        _workflow = workflow;
        _bills = bills;
        _events = events;
        _approvals = approvals;
    }

    public async Task<Result<BillDetailDto>> Handle(RefundPaymentCommand command, CancellationToken cancellationToken)
    {
        var (id, request, idempotencyKey) = (command.Id, command.Input, command.IdempotencyKey);
        if (await _db.Payments.AnyAsync(p => p.IdempotencyKey == idempotencyKey, cancellationToken))
        {
            return await _bills.GetAsync(id, cancellationToken);
        }

        var approvedBy = 0;
        int? shiftId = null;
        return await _workflow.RunAsync(id, request.RowVersion, claim: false,
            prepare: async scope =>
            {
                // Cash is paid back from the drawer of the terminal's open shift.
                var original = scope.Bill.Payments.FirstOrDefault(p => p.Id == request.PaymentId && !p.IsRefund);
                if (original is not null
                    && await _db.PaymentMethods.AsNoTracking().Where(m => m.Id == original.PaymentMethodId).Select(m => m.Code).FirstOrDefaultAsync(cancellationToken) == PaymentMethodCodes.Cash)
                {
                    shiftId = _currentUser.DeviceId is { } deviceId ? await _shifts.LockOpenShiftOfDeviceAsync(deviceId, cancellationToken) : null;
                    if (shiftId is null)
                    {
                        return AppErrors.ShiftRequired();
                    }
                }

                var approval = await _approvals.ApproveAsync(request.Approval, ApprovalActions.Refund, "a refund", cancellationToken);
                approvedBy = approval.IsSuccess ? approval.Value : 0;
                return approval.Error;
            },
            step: scope =>
            {
                var bill = scope.Bill;
                var refund = bill.Refund(request.PaymentId, request.Amount, request.Reason, _bills.UserId, _currentUser.DeviceId, idempotencyKey, _clock.UtcNow);
                if (shiftId is { } cashShiftId)
                {
                    refund.RecordInShift(cashShiftId);
                }

                _audit.Add(AuditActions.PaymentRefunded, nameof(Bill), bill.Id.ToString(CultureInfo.InvariantCulture), newValues: new
                {
                    bill.InvoiceNumber,
                    PaymentId = request.PaymentId,
                    Amount = -refund.Amount,
                    refund.RefundReason,
                    bill.RefundedAmount,
                    ApprovedBy = approvedBy,
                });
                _afterCommit.Enqueue(() => _events.PublishBillUpdatedAsync(bill, BillChangeTypes.Refunded));
                return Task.FromResult<AppError?>(null);
            },
            cancellationToken);
    }
}
