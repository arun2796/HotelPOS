using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Contracts.Realtime;

namespace HotelPOS.Application.Features.Menu;

// Every menu write ends here: the version bump runs inside the command's transaction (its row lock serialises
// concurrent menu writes) and MenuChanged goes out once the change is committed.
internal sealed class MenuChanges
{
    private readonly IAppDbContext _db;
    private readonly IMenuVersionStore _versions;
    private readonly IRealtimeNotifier _realtime;
    private readonly IClock _clock;
    private readonly IAfterCommit _afterCommit;

    public MenuChanges(IAppDbContext db, IMenuVersionStore versions, IRealtimeNotifier realtime, IClock clock, IAfterCommit afterCommit)
    {
        _db = db;
        _versions = versions;
        _realtime = realtime;
        _clock = clock;
        _afterCommit = afterCommit;
    }

    public async Task<int> SaveAsync(CancellationToken cancellationToken)
    {
        await _db.SaveChangesAsync(cancellationToken);
        var version = await _versions.BumpAsync(cancellationToken);

        // Not tied to the request: a client that hung up must not stop others from being notified.
        _afterCommit.Enqueue(() => _realtime.PublishAsync(HubEvents.MenuChanged, new MenuChangedEvent
        {
            OccurredAtUtc = _clock.UtcNow,
            MenuVersion = version,
            EntityVersion = version.ToString(System.Globalization.CultureInfo.InvariantCulture),
        }, RealtimeAudience.All, CancellationToken.None));

        return version;
    }
}
