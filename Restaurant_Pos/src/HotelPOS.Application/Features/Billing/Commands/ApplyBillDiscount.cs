using System.Globalization;
using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Billing;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Billing.Commands;

// A predefined or manual discount; above the cashier limit (or when the discount says so) a manager approves it.
public sealed record ApplyBillDiscountCommand(int Id, ApplyDiscountRequest Input)
    : ICommand<BillDetailDto>, IHasInput<ApplyDiscountRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.BillingDiscount;
}

public sealed class ApplyDiscountRequestValidator : AbstractValidator<ApplyDiscountRequest>
{
    public ApplyDiscountRequestValidator()
    {
        RuleFor(x => x.RowVersion).NotEmpty();
        RuleFor(x => x.Reason).MaximumLength(BillingLimits.ReasonMaxLength);
        When(x => x.DiscountId is null, () =>
        {
            RuleFor(x => x.Type).NotNull().IsInEnum().WithMessage("Choose a percentage or a fixed amount.");
            RuleFor(x => x.Value).NotNull().GreaterThan(0).LessThanOrEqualTo(BillingLimits.MaxAmount).PrecisionScale(18, 2, true);
            RuleFor(x => x.Value).LessThanOrEqualTo(100).When(x => x.Type == DiscountType.Percentage)
                .WithMessage("A percentage discount cannot exceed 100%.");
            RuleFor(x => x.Reason).NotEmpty().WithMessage("Enter the reason for a manual discount.");
        });
        RuleFor(x => x.DiscountId).GreaterThan(0).When(x => x.DiscountId is not null);
        RuleFor(x => x.Approval!).SetValidator(new ManagerApprovalDtoValidator()).When(x => x.Approval is not null);
    }
}

internal sealed class ApplyBillDiscountHandler : ICommandHandler<ApplyBillDiscountCommand, BillDetailDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly IAfterCommit _afterCommit;
    private readonly BillWorkflow _workflow;
    private readonly BillSteps _bills;
    private readonly BillEvents _events;
    private readonly ManagerApprovals _approvals;

    public ApplyBillDiscountHandler(
        IAppDbContext db,
        IAuditTrail audit,
        IAfterCommit afterCommit,
        BillWorkflow workflow,
        BillSteps bills,
        BillEvents events,
        ManagerApprovals approvals)
    {
        _db = db;
        _audit = audit;
        _afterCommit = afterCommit;
        _workflow = workflow;
        _bills = bills;
        _events = events;
        _approvals = approvals;
    }

    public async Task<Result<BillDetailDto>> Handle(ApplyBillDiscountCommand command, CancellationToken cancellationToken)
    {
        var request = command.Input;
        int? approvedBy = null;
        Discount? predefined = null;
        DiscountType type = default;
        decimal value = 0;

        return await _workflow.RunAsync(command.Id, request.RowVersion, claim: true,
            prepare: async scope =>
            {
                bool needsApproval;
                if (request.DiscountId is { } discountId)
                {
                    predefined = await _db.Discounts.AsNoTracking().FirstOrDefaultAsync(d => d.Id == discountId, cancellationToken);
                    if (predefined is not { IsActive: true })
                    {
                        return AppErrors.Validation("discountId", "That discount is not available.");
                    }

                    (type, value, needsApproval) = (predefined.Type, predefined.Value, predefined.RequiresApproval);
                }
                else
                {
                    (type, value) = (request.Type!.Value, request.Value!.Value);
                    var limit = await _db.DecimalSettingAsync(SettingKeys.MaxCashierDiscountPercent, 10m, cancellationToken);
                    needsApproval = BillCalculator.DiscountPercentOf(new BillDiscountInput(type, value), scope.Bill.Subtotal) > limit;
                }

                if (needsApproval)
                {
                    var approval = await _approvals.ApproveAsync(request.Approval, ApprovalActions.Discount, "this discount", cancellationToken);
                    if (approval.IsFailure)
                    {
                        return approval.Error;
                    }

                    approvedBy = approval.Value;
                }

                return null;
            },
            step: async scope =>
            {
                var bill = scope.Bill;
                var before = new { bill.DiscountType, bill.DiscountValue, bill.DiscountAmount, bill.GrandTotal };
                bill.ApplyDiscount(predefined?.Id, type, value, request.Reason ?? predefined?.Name, approvedBy, await _bills.RoundOffAsync(cancellationToken));
                _audit.Add(AuditActions.BillDiscountApplied, nameof(Bill), bill.Id.ToString(CultureInfo.InvariantCulture), before,
                    new { bill.DiscountType, bill.DiscountValue, bill.DiscountAmount, bill.GrandTotal, bill.DiscountReason, ApprovedBy = approvedBy });
                _afterCommit.Enqueue(() => _events.PublishBillUpdatedAsync(bill, BillChangeTypes.DiscountChanged));
                return null;
            },
            cancellationToken);
    }
}
