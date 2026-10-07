using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Contracts.Realtime;
using Microsoft.EntityFrameworkCore.Storage;

namespace HotelPOS.Application.Menu;

public sealed class MenuChanges
{
    private readonly IAppDbContext _db;
    private readonly IMenuVersionStore _versions;
    private readonly IRealtimeNotifier _realtime;
    private readonly IClock _clock;

    public MenuChanges(IAppDbContext db, IMenuVersionStore versions, IRealtimeNotifier realtime, IClock clock)
    {
        _db = db;
        _versions = versions;
        _realtime = realtime;
        _clock = clock;
    }

    public Task<IDbContextTransaction> BeginAsync(CancellationToken cancellationToken) =>
        _db.Database.BeginTransactionAsync(cancellationToken);

    public async Task<int> CommitAsync(IDbContextTransaction transaction, CancellationToken cancellationToken)
    {
        await _db.SaveChangesAsync(cancellationToken);
        var version = await _versions.BumpAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // Not tied to the request: a client that hung up must not stop others from being notified.
        await _realtime.PublishAsync(HubEvents.MenuChanged, new MenuChangedEvent
        {
            OccurredAtUtc = _clock.UtcNow,
            MenuVersion = version,
            EntityVersion = version.ToString(System.Globalization.CultureInfo.InvariantCulture),
        }, RealtimeAudience.All, CancellationToken.None);

        return version;
    }
}
