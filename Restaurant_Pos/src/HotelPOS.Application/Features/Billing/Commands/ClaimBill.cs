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

// A counter takes the bill so other counters see "being handled by ..."; a manager may take it over.
public sealed record ClaimBillCommand(int Id, ClaimBillRequest Input)
    : ICommand<BillDetailDto>, IHasInput<ClaimBillRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.BillingOperate;
}

internal sealed class ClaimBillHandler : ICommandHandler<ClaimBillCommand, BillDetailDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly IAfterCommit _afterCommit;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly BillSteps _bills;
    private readonly BillEvents _events;

    public ClaimBillHandler(
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

    public async Task<Result<BillDetailDto>> Handle(ClaimBillCommand command, CancellationToken cancellationToken)
    {
        var id = command.Id;
        var bill = await _db.Bills.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (bill is null)
        {
            return AppErrors.NotFound("Bill", id);
        }

        if (!bill.IsPending)
        {
            return AppErrors.InvalidState($"Bill {bill.BillNumber} is {bill.Status} and needs no handling.", await _bills.DetailAsync(id, cancellationToken));
        }

        var now = _clock.UtcNow;
        var userId = _bills.UserId;
        var heldByOther = bill.IsHeldByOther(userId, _currentUser.DeviceId, now);
        if (heldByOther && !(command.Input.Override && _bills.IsManager))
        {
            return AppErrors.BillClaimed(await _bills.HolderAsync(bill, cancellationToken), await _bills.DetailAsync(id, cancellationToken));
        }

        var wasMine = bill.IsHeldBy(userId, _currentUser.DeviceId, now);
        var previousHolder = heldByOther ? await _bills.HolderAsync(bill, cancellationToken) : null;
        bill.Claim(userId, _currentUser.DeviceId, now);
        if (heldByOther)
        {
            _audit.Add(AuditActions.BillClaimOverridden, nameof(Bill), bill.Id.ToString(CultureInfo.InvariantCulture),
                new { Holder = previousHolder }, new { Holder = _currentUser.DeviceName ?? _currentUser.UserName });
        }
        else if (!wasMine)
        {
            _audit.Add(AuditActions.BillClaimed, nameof(Bill), bill.Id.ToString(CultureInfo.InvariantCulture),
                newValues: new { bill.BillNumber, Holder = _currentUser.DeviceName ?? _currentUser.UserName });
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Two counters opened the bill at the same moment; the other one won.
            var current = await _db.Bills.AsNoTracking().FirstAsync(b => b.Id == id, cancellationToken);
            return current.IsHeldByOther(userId, _currentUser.DeviceId, now)
                ? AppErrors.BillClaimed(await _bills.HolderAsync(current, cancellationToken), await _bills.DetailAsync(id, cancellationToken))
                : AppErrors.Concurrency("bill", await _bills.DetailAsync(id, cancellationToken));
        }

        if (!wasMine)
        {
            _afterCommit.Enqueue(() => _events.PublishBillUpdatedAsync(bill, BillChangeTypes.Claimed));
        }

        return await _bills.GetAsync(id, cancellationToken);
    }
}
