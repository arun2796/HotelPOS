using System.Globalization;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Billing;

namespace HotelPOS.Application.Features.Billing.Commands;

// The row version comes from the query string and has never been validated: a missing one is a conflict (409), not
// a validation error, so this command deliberately carries no DTO.
public sealed record ClearBillDiscountCommand(int Id, string RowVersion) : ICommand<BillDetailDto>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.BillingDiscount;
}

internal sealed class ClearBillDiscountHandler : ICommandHandler<ClearBillDiscountCommand, BillDetailDto>
{
    private readonly IAuditTrail _audit;
    private readonly IAfterCommit _afterCommit;
    private readonly BillWorkflow _workflow;
    private readonly BillSteps _bills;
    private readonly BillEvents _events;

    public ClearBillDiscountHandler(IAuditTrail audit, IAfterCommit afterCommit, BillWorkflow workflow, BillSteps bills, BillEvents events)
    {
        _audit = audit;
        _afterCommit = afterCommit;
        _workflow = workflow;
        _bills = bills;
        _events = events;
    }

    public Task<Result<BillDetailDto>> Handle(ClearBillDiscountCommand command, CancellationToken cancellationToken) =>
        _workflow.RunAsync(command.Id, command.RowVersion, claim: true, prepare: null,
            step: async scope =>
            {
                var bill = scope.Bill;
                var before = new { bill.DiscountType, bill.DiscountValue, bill.DiscountAmount, bill.GrandTotal };
                bill.ClearDiscount(await _bills.RoundOffAsync(cancellationToken));
                _audit.Add(AuditActions.BillDiscountCleared, nameof(Bill), bill.Id.ToString(CultureInfo.InvariantCulture), before, new { bill.GrandTotal });
                _afterCommit.Enqueue(() => _events.PublishBillUpdatedAsync(bill, BillChangeTypes.DiscountChanged));
                return null;
            },
            cancellationToken);
}
