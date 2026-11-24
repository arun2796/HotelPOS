using System.Globalization;
using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Orders;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Billing;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Billing.Commands;

// A table of six guests asks for separate bills: the cashier moves the lines of each guest (or seat group) to a bill of
// its own. Each part is finalized and paid on its own with its own invoice number; the table is freed when the last
// part is paid. Only an open (not yet finalized) bill without payments can be split.
public sealed record SplitBillCommand(int Id, SplitBillRequest Input)
    : ICommand<BillDetailDto>, IHasInput<SplitBillRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.BillingOperate;
}

public sealed class SplitBillRequestValidator : AbstractValidator<SplitBillRequest>
{
    public SplitBillRequestValidator()
    {
        RuleFor(x => x.RowVersion).NotEmpty();
        RuleFor(x => x.Parts).NotEmpty().WithMessage("Choose what moves to the new bill.");
        RuleFor(x => x.Parts.Count).LessThan(BillParts.MaxParts);
        RuleForEach(x => x.Parts).ChildRules(part =>
            part.RuleFor(p => p.BillItemIds).NotEmpty().WithMessage("Each new bill needs at least one line."));
    }
}

internal sealed class SplitBillHandler : ICommandHandler<SplitBillCommand, BillDetailDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly IAfterCommit _afterCommit;
    private readonly BillWorkflow _workflow;
    private readonly BillSteps _bills;
    private readonly BillEvents _events;

    public SplitBillHandler(IAppDbContext db, IAuditTrail audit, IAfterCommit afterCommit, BillWorkflow workflow, BillSteps bills, BillEvents events)
    {
        _db = db;
        _audit = audit;
        _afterCommit = afterCommit;
        _workflow = workflow;
        _bills = bills;
        _events = events;
    }

    public async Task<Result<BillDetailDto>> Handle(SplitBillCommand command, CancellationToken cancellationToken)
    {
        var request = command.Input;
        return await _workflow.RunAsync(command.Id, request.RowVersion, claim: true, prepare: null,
            step: async scope =>
            {
                var (bill, order, table) = (scope.Bill, scope.Order, scope.Table);
                var live = await _db.Bills.Where(b => b.OrderId == order.Id && b.Status != BillStatus.Voided)
                    .Select(b => b.SplitIndex).ToListAsync(cancellationToken);
                if (live.Count + request.Parts.Count > BillParts.MaxParts)
                {
                    return AppErrors.BusinessRule($"A table's bill can be split into at most {BillParts.MaxParts} bills.");
                }

                var baseCode = OrderChannels.TableLabel(order.OrderType, order.OrderNumber, table?.Code);
                var roundOff = await _bills.RoundOffAsync(cancellationToken);
                var next = live.Max();
                var created = new List<Bill>();
                foreach (var part in request.Parts)
                {
                    var sibling = bill.SplitOff(part.BillItemIds, ++next, roundOff);
                    sibling.LabelPart(baseCode, sibling.SplitIndex);
                    _db.Bills.Add(sibling);
                    created.Add(sibling);
                }

                // The first split gives the original bill its letter too ("T04 (A)").
                if (bill.SplitIndex == 0 && live.Count == 1)
                {
                    bill.LabelPart(baseCode, 0);
                }

                _audit.Add(AuditActions.BillSplit, nameof(Bill), bill.Id.ToString(CultureInfo.InvariantCulture), newValues: new
                {
                    bill.BillNumber,
                    order.OrderNumber,
                    Parts = created.Select(c => new { Part = BillParts.Letter(c.SplitIndex), Lines = c.Items.Count, c.GrandTotal }).ToList(),
                    Remaining = bill.GrandTotal,
                });
                foreach (var sibling in created)
                {
                    var newBill = sibling;
                    _afterCommit.Enqueue(() => _events.PublishRequestedAsync(newBill, order, newBill.TableCode));
                }

                _afterCommit.Enqueue(() => _events.PublishBillUpdatedAsync(bill, BillChangeTypes.Split));
                return null;
            },
            cancellationToken);
    }
}
