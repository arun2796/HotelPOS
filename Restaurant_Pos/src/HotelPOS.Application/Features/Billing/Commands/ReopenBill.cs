using System.Globalization;
using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Features.Floor;
using HotelPOS.Application.Features.Kitchen;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Billing;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Billing.Commands;

// Lets the order be changed again. A finalized bill has an invoice number; reopening cancels that invoice and needs
// an approval.
public sealed record ReopenBillCommand(int Id, ReopenBillRequest Input)
    : ICommand<BillDetailDto>, IHasInput<ReopenBillRequest>, IRequirePermission, IAuditedCommand, IBusinessDayGuarded
{
    public BusinessDayScope BusinessDayScope => new(Id);

    public string Permission => Permissions.BillingOperate;
}

public sealed class ReopenBillRequestValidator : AbstractValidator<ReopenBillRequest>
{
    public ReopenBillRequestValidator()
    {
        RuleFor(x => x.RowVersion).NotEmpty();
        RuleFor(x => x.Reason).MaximumLength(BillingLimits.ReasonMaxLength);
        RuleFor(x => x.Approval!).SetValidator(new ManagerApprovalDtoValidator()).When(x => x.Approval is not null);
    }
}

internal sealed class ReopenBillHandler : ICommandHandler<ReopenBillCommand, BillDetailDto>
{
    private readonly IAuditTrail _audit;
    private readonly IAfterCommit _afterCommit;
    private readonly IClock _clock;
    private readonly BillWorkflow _workflow;
    private readonly BillEvents _events;
    private readonly ManagerApprovals _approvals;
    private readonly KitchenSync _kitchen;
    private readonly TableEvents _tableEvents;
    private readonly IAppDbContext _db;

    public ReopenBillHandler(
        IAppDbContext db,
        IAuditTrail audit,
        IAfterCommit afterCommit,
        IClock clock,
        BillWorkflow workflow,
        BillEvents events,
        ManagerApprovals approvals,
        KitchenSync kitchen,
        TableEvents tableEvents)
    {
        _db = db;
        _audit = audit;
        _afterCommit = afterCommit;
        _clock = clock;
        _workflow = workflow;
        _events = events;
        _approvals = approvals;
        _kitchen = kitchen;
        _tableEvents = tableEvents;
    }

    public async Task<Result<BillDetailDto>> Handle(ReopenBillCommand command, CancellationToken cancellationToken)
    {
        var request = command.Input;
        int? approvedBy = null;
        return await _workflow.RunAsync(command.Id, request.RowVersion, claim: true,
            prepare: async scope =>
            {
                if (scope.Bill.Status != BillStatus.Finalized)
                {
                    return null;
                }

                var approval = await _approvals.ApproveAsync(request.Approval, ApprovalActions.Reopen, "reopening an invoiced bill", cancellationToken);
                approvedBy = approval.IsSuccess ? approval.Value : null;
                return approval.Error;
            },
            step: async scope =>
            {
                var (bill, order, table) = (scope.Bill, scope.Order, scope.Table);
                var tableBefore = table?.Status;

                // A split bill is undone as a whole, and only while no part has been finalized or paid.
                var parts = await _db.Bills.Where(b => b.OrderId == order.Id && b.Id != bill.Id && b.Status != BillStatus.Voided).ToListAsync(cancellationToken);
                if (parts.Count > 0)
                {
                    if (bill.Status != BillStatus.Open || parts.Any(p => p.Status != BillStatus.Open || p.PaidAmount > 0))
                    {
                        return AppErrors.BusinessRule("This table's bill is split. It can be reopened only while every part is still open; void the finalized parts instead.");
                    }

                    foreach (var part in parts)
                    {
                        part.Reopen("Split bill undone", _clock.UtcNow);
                        var undone = part;
                        _afterCommit.Enqueue(() => _events.PublishBillUpdatedAsync(undone, BillChangeTypes.Reopened));
                    }
                }

                bill.Reopen(request.Reason ?? "Reopened for changes", _clock.UtcNow);
                order.ReopenFromBill();
                var tickets = await _kitchen.LoadTicketsAsync(order.Id, cancellationToken);
                _kitchen.Recompute(order, tickets, table);
                _audit.Add(AuditActions.BillReopened, nameof(Bill), bill.Id.ToString(CultureInfo.InvariantCulture), newValues: new
                {
                    bill.BillNumber,
                    CancelledInvoice = bill.InvoiceNumber,
                    order.OrderNumber,
                    Reason = bill.VoidReason,
                    ApprovedBy = approvedBy,
                });
                _afterCommit.Enqueue(() => _events.PublishBillUpdatedAsync(bill, BillChangeTypes.Reopened));
                _afterCommit.Enqueue(() => _events.PublishOrderUpdatedAsync(order, OrderChangeTypes.BillReopened));
                if (table != null && table.Status != tableBefore)
                {
                    _afterCommit.Enqueue(() => _tableEvents.PublishStatusAsync(table, order.OrderNumber));
                }

                return null;
            },
            cancellationToken);
    }
}
