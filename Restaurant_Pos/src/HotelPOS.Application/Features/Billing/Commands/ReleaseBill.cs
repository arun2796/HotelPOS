using System.Globalization;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Billing;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Billing.Commands;

public sealed record ReleaseBillCommand(int Id) : ICommand<BillDetailDto>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.BillingOperate;
}

internal sealed class ReleaseBillHandler : ICommandHandler<ReleaseBillCommand, BillDetailDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly IAfterCommit _afterCommit;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly BillSteps _bills;
    private readonly BillEvents _events;

    public ReleaseBillHandler(
        IAppDbContext db,
        IAuditTrail audit,
        IAfterCommit afterCommit,
        ICurrentUser currentUser,
        IClock clock,
        BillSteps bills,
        BillEvents events)
    {
        _db = db;
        _audit = audit;
        _afterCommit = afterCommit;
        _currentUser = currentUser;
        _clock = clock;
        _bills = bills;
        _events = events;
    }

    public async Task<Result<BillDetailDto>> Handle(ReleaseBillCommand command, CancellationToken cancellationToken)
    {
        var id = command.Id;
        var bill = await _db.Bills.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (bill is null)
        {
            return AppErrors.NotFound("Bill", id);
        }

        var now = _clock.UtcNow;
        if (bill.ClaimExpiresAt is null || bill.ClaimExpiresAt <= now)
        {
            return await _bills.GetAsync(id, cancellationToken);
        }

        if (bill.IsHeldByOther(_bills.UserId, _currentUser.DeviceId, now) && !_bills.IsManager)
        {
            return AppErrors.BillClaimed(await _bills.HolderAsync(bill, cancellationToken), await _bills.DetailAsync(id, cancellationToken));
        }

        bill.Release();
        _audit.Add(AuditActions.BillReleased, nameof(Bill), bill.Id.ToString(CultureInfo.InvariantCulture), newValues: new { bill.BillNumber });
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return AppErrors.Concurrency("bill", await _bills.DetailAsync(id, cancellationToken));
        }

        _afterCommit.Enqueue(() => _events.PublishBillUpdatedAsync(bill, BillChangeTypes.Released));
        return await _bills.GetAsync(id, cancellationToken);
    }
}
