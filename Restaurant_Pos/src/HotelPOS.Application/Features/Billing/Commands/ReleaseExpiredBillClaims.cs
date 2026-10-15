using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Realtime;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Billing.Commands;

// Sent every minute by the server itself (BillClaimCleanupService): no user, no permission, nothing to audit.
// Expired claims are already ignored by every check; clearing them lets the queues drop the "being handled" badge.
public sealed record ReleaseExpiredBillClaimsCommand : ICommand<int>;

internal sealed class ReleaseExpiredBillClaimsHandler : ICommandHandler<ReleaseExpiredBillClaimsCommand, int>
{
    private readonly IAppDbContext _db;
    private readonly IAfterCommit _afterCommit;
    private readonly IClock _clock;
    private readonly BillEvents _events;

    public ReleaseExpiredBillClaimsHandler(IAppDbContext db, IAfterCommit afterCommit, IClock clock, BillEvents events)
    {
        _db = db;
        _afterCommit = afterCommit;
        _clock = clock;
        _events = events;
    }

    public async Task<Result<int>> Handle(ReleaseExpiredBillClaimsCommand command, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var expired = await _db.Bills.Where(b => b.ClaimExpiresAt != null && b.ClaimExpiresAt <= now).ToListAsync(cancellationToken);
        foreach (var bill in expired)
        {
            bill.Release();
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A cashier touched one of them meanwhile; the next run picks up whatever is still expired.
            return 0;
        }

        foreach (var bill in expired)
        {
            _afterCommit.Enqueue(() => _events.PublishBillUpdatedAsync(bill, BillChangeTypes.Released));
        }

        return expired.Count;
    }
}
