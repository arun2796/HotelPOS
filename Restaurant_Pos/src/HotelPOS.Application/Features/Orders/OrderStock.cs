using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Orders;

// The kitchen's stock count (idlis per steaming vessel, portions of biryani). "Process" commits the order, so that is
// when the count goes down; cancelling a sent dish puts the portions back. An item that reaches zero is switched off
// on every captain's menu through the usual MenuChanged event.
//
// The count changes with one conditional UPDATE per item, not through the tracked entity: two captains sending the
// last portions at the same moment queue on the row lock and the second one is told what is left, instead of one of
// them failing with a "changed by another user" error. A refused order rolls the whole command back.
internal sealed class OrderStock
{
    private readonly IAppDbContext _db;
    private readonly IMenuVersionStore _versions;
    private readonly IRealtimeNotifier _realtime;
    private readonly IClock _clock;
    private readonly IAfterCommit _afterCommit;

    public OrderStock(IAppDbContext db, IMenuVersionStore versions, IRealtimeNotifier realtime, IClock clock, IAfterCommit afterCommit)
    {
        _db = db;
        _versions = versions;
        _realtime = realtime;
        _clock = clock;
        _afterCommit = afterCommit;
    }

    // Returns the error to show the captain, or null when the order may go to the kitchen.
    public async Task<AppError?> ReserveAsync(IEnumerable<OrderItem> items, CancellationToken cancellationToken)
    {
        var wanted = Quantities(items);
        if (wanted.Count == 0)
        {
            return null;
        }

        var changed = false;
        foreach (var (id, quantity) in wanted)
        {
            var item = await _db.MenuItems.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
            if (item is null)
            {
                continue;
            }

            if (!item.IsAvailable)
            {
                return AppErrors.BusinessRule($"{item.Name} has just run out. Tell the guest and pick another dish.");
            }

            if (item.StockMode == StockTrackingMode.None)
            {
                continue;
            }

            // Exact: only while enough is left. Approximate: always, the count just stops at zero.
            var rows = item.StockMode == StockTrackingMode.Exact
                ? await _db.MenuItems
                    .Where(m => m.Id == id && m.IsAvailable && m.StockMode == StockTrackingMode.Exact && m.StockRemaining >= quantity)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(m => m.StockRemaining, m => m.StockRemaining - quantity)
                        .SetProperty(m => m.IsAvailable, m => m.StockRemaining - quantity > 0), cancellationToken)
                : await _db.MenuItems
                    .Where(m => m.Id == id && m.IsAvailable && m.StockMode == StockTrackingMode.Approximate)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(m => m.StockRemaining, m => m.StockRemaining - quantity < 0 ? 0 : m.StockRemaining - quantity)
                        .SetProperty(m => m.IsAvailable, m => m.StockRemaining - quantity > 0), cancellationToken);
            if (rows == 1)
            {
                changed = true;
                continue;
            }

            // Somebody else took the last portions between the check and the update: say what is left now.
            var now = await _db.MenuItems.AsNoTracking().FirstAsync(m => m.Id == id, cancellationToken);
            return !now.IsAvailable || (now.StockRemaining ?? 0) <= 0
                ? AppErrors.BusinessRule($"{now.Name} has just run out. Tell the guest and pick another dish.")
                : AppErrors.BusinessRule($"Only {now.StockRemaining} {now.Name} left, but {quantity} were ordered. Reduce the quantity or pick another dish.");
        }

        if (changed)
        {
            await PublishMenuChangeAsync(cancellationToken);
        }

        return null;
    }

    // Dishes that were with the kitchen and were cancelled: the portions go back.
    public async Task RestoreAsync(IEnumerable<OrderItem> items, CancellationToken cancellationToken)
    {
        var wanted = Quantities(items);
        var changed = false;
        foreach (var (id, quantity) in wanted)
        {
            var rows = await _db.MenuItems
                .Where(m => m.Id == id && m.StockMode != StockTrackingMode.None && m.StockRemaining != null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.IsAvailable, m => m.IsAvailable || m.StockRemaining == 0)
                    .SetProperty(m => m.StockRemaining, m => m.StockRemaining + quantity), cancellationToken);
            changed |= rows > 0;
        }

        if (changed)
        {
            await PublishMenuChangeAsync(cancellationToken);
        }
    }

    // The supervisor changed a count or a switch: every client reloads the menu.
    public async Task PublishMenuChangeAsync(CancellationToken cancellationToken)
    {
        var version = await _versions.BumpAsync(cancellationToken);
        _afterCommit.Enqueue(() => _realtime.PublishAsync(HubEvents.MenuChanged, new MenuChangedEvent
        {
            OccurredAtUtc = _clock.UtcNow,
            MenuVersion = version,
            EntityVersion = version.ToString(System.Globalization.CultureInfo.InvariantCulture),
        }, RealtimeAudience.All, CancellationToken.None));
    }

    private static Dictionary<int, int> Quantities(IEnumerable<OrderItem> items) =>
        items.Where(i => i.Status != OrderItemStatus.Cancelled)
            .GroupBy(i => i.MenuItemId)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));
}
