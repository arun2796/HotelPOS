using System.Globalization;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Billing;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Billing.Commands;

// Completes a paid order when the venue does not close orders automatically on full payment.
public sealed record CloseBillCommand(int Id) : ICommand<BillDetailDto>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.BillingOperate;
}

internal sealed class CloseBillHandler : ICommandHandler<CloseBillCommand, BillDetailDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly IAfterCommit _afterCommit;
    private readonly IClock _clock;
    private readonly BillSteps _bills;
    private readonly BillEvents _events;

    public CloseBillHandler(IAppDbContext db, IAuditTrail audit, IAfterCommit afterCommit, IClock clock, BillSteps bills, BillEvents events)
    {
        _db = db;
        _audit = audit;
        _afterCommit = afterCommit;
        _clock = clock;
        _bills = bills;
        _events = events;
    }

    public async Task<Result<BillDetailDto>> Handle(CloseBillCommand command, CancellationToken cancellationToken)
    {
        var id = command.Id;
        var scope = await _bills.LoadAsync(id, cancellationToken);
        if (scope is null)
        {
            return AppErrors.NotFound("Bill", id);
        }

        if (scope.Bill.Status != BillStatus.Settled || scope.Order.Status != OrderStatus.Paid)
        {
            return AppErrors.InvalidState($"Order {scope.Order.OrderNumber} is {scope.Order.Status} and cannot be closed.", await _bills.DetailAsync(id, cancellationToken));
        }

        scope.Order.Close(_clock.UtcNow);
        _audit.Add(AuditActions.BillClosed, nameof(Bill), scope.Bill.Id.ToString(CultureInfo.InvariantCulture), newValues: new { scope.Order.OrderNumber });
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return AppErrors.Concurrency("order", await _bills.DetailAsync(id, cancellationToken));
        }

        _afterCommit.Enqueue(() => _events.PublishOrderUpdatedAsync(scope.Order, OrderChangeTypes.Completed));
        return await _bills.GetAsync(id, cancellationToken);
    }
}
