using System.Globalization;
using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Features.Floor;
using HotelPOS.Application.Features.Kitchen;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Billing;

namespace HotelPOS.Application.Features.Billing.Commands;

// Cancels the bill and its order. Cashiers may send it too: it then needs a manager's approval in the request.
public sealed record VoidBillCommand(int Id, VoidBillRequest Input)
    : ICommand<BillDetailDto>, IHasInput<VoidBillRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.BillingOperate;
}

public sealed class VoidBillRequestValidator : AbstractValidator<VoidBillRequest>
{
    public VoidBillRequestValidator()
    {
        RuleFor(x => x.RowVersion).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Enter the reason for voiding the bill.").MaximumLength(BillingLimits.ReasonMaxLength);
        RuleFor(x => x.Approval!).SetValidator(new ManagerApprovalDtoValidator()).When(x => x.Approval is not null);
    }
}

internal sealed class VoidBillHandler : ICommandHandler<VoidBillCommand, BillDetailDto>
{
    private readonly IAuditTrail _audit;
    private readonly IAfterCommit _afterCommit;
    private readonly IClock _clock;
    private readonly BillWorkflow _workflow;
    private readonly BillSteps _bills;
    private readonly BillEvents _events;
    private readonly ManagerApprovals _approvals;
    private readonly KitchenSync _kitchen;
    private readonly TableEvents _tableEvents;

    public VoidBillHandler(
        IAuditTrail audit,
        IAfterCommit afterCommit,
        IClock clock,
        BillWorkflow workflow,
        BillSteps bills,
        BillEvents events,
        ManagerApprovals approvals,
        KitchenSync kitchen,
        TableEvents tableEvents)
    {
        _audit = audit;
        _afterCommit = afterCommit;
        _clock = clock;
        _workflow = workflow;
        _bills = bills;
        _events = events;
        _approvals = approvals;
        _kitchen = kitchen;
        _tableEvents = tableEvents;
    }

    public async Task<Result<BillDetailDto>> Handle(VoidBillCommand command, CancellationToken cancellationToken)
    {
        var request = command.Input;
        var approvedBy = 0;
        return await _workflow.RunAsync(command.Id, request.RowVersion, claim: true,
            prepare: async _ =>
            {
                var approval = await _approvals.ApproveAsync(request.Approval, ApprovalActions.Void, "voiding a bill", cancellationToken);
                approvedBy = approval.IsSuccess ? approval.Value : 0;
                return approval.Error;
            },
            step: async scope =>
            {
                var (bill, order, table) = (scope.Bill, scope.Order, scope.Table);
                var now = _clock.UtcNow;
                bill.Void(request.Reason, now);
                var openTickets = (await _kitchen.LoadTicketsAsync(order.Id, cancellationToken)).Where(t => t.IsOpen).ToList();
                foreach (var ticket in openTickets)
                {
                    ticket.Cancel(now);
                }

                order.Cancel(_bills.UserId, request.Reason, now);
                if (table.CurrentOrderId == order.Id)
                {
                    table.DetachOrder(keepGuestsSeated: false);
                }

                _audit.Add(AuditActions.BillVoided, nameof(Bill), bill.Id.ToString(CultureInfo.InvariantCulture), newValues: new
                {
                    bill.BillNumber,
                    bill.InvoiceNumber,
                    bill.GrandTotal,
                    order.OrderNumber,
                    Reason = bill.VoidReason,
                    ApprovedBy = approvedBy,
                });
                _afterCommit.Enqueue(() => _events.PublishBillUpdatedAsync(bill, BillChangeTypes.Voided));
                _afterCommit.Enqueue(() => _events.PublishOrderCancelledAsync(order, table.Code));
                _afterCommit.Enqueue(() => _tableEvents.PublishStatusAsync(table));
                _afterCommit.Enqueue(() => _events.PublishTicketsAsync(order, table, openTickets));
                return null;
            },
            cancellationToken);
    }
}
