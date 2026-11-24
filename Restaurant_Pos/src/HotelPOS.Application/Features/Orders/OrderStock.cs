using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Domain.Menu;
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

    // Ledger rows written before their order exists (a new order has no id yet): LinkPending ties them to it.
    private readonly List<StockMovement> _unlinked = new();

    public OrderStock(IAppDbContext db, IMenuVersionStore versions, IRealtimeNotifier realtime, IClock clock, IAfterCommit afterCommit)
    {
        _db = db;
        _versions = versions;
        _realtime = realtime;
        _clock = clock;
        _afterCommit = afterCommit;
    }

    // Returns the error to show the captain, or null when the order may go to the kitchen.
    public async Task<AppError?> ReserveAsync(IEnumerable<OrderItem> items, CancellationToken cancellationToken, int? orderId = null)
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
                var after = await _db.MenuItems.AsNoTracking().Where(m => m.Id == id).Select(m => m.StockRemaining ?? 0).FirstAsync(cancellationToken);
                var taken = item.StockMode == StockTrackingMode.Exact ? quantity : Math.Min(quantity, item.StockRemaining ?? quantity);
                Record(id, StockMovementType.OrderReservation, after + taken, -taken, after, orderId, null);
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
    public async Task RestoreAsync(IEnumerable<OrderItem> items, CancellationToken cancellationToken, int? orderId = null, string? remarks = null)
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
            if (rows > 0)
            {
                var after = await _db.MenuItems.AsNoTracking().Where(m => m.Id == id).Select(m => m.StockRemaining ?? 0).FirstAsync(cancellationToken);
                Record(id, StockMovementType.OrderRelease, after - quantity, quantity, after, orderId, remarks);
            }
        }

        if (changed)
        {
            await PublishMenuChangeAsync(cancellationToken);
        }
    }

    // A bill was voided or refunded: the food that was with the kitchen goes back on the count (Restock) or is written off
    // (Waste: put back and taken off again, so the ledger shows both and the count stays as it is). With no choice, a dish
    // the kitchen had not started goes back and one it had started is written off.
    public async Task DisposeAsync(
        IEnumerable<(OrderItem Item, bool Started)> lines,
        StockDisposition? choice,
        string reason,
        int orderId,
        CancellationToken cancellationToken)
    {
        var restock = new Dictionary<int, int>();
        var waste = new Dictionary<int, int>();
        foreach (var (item, started) in lines)
        {
            if (item.Status == OrderItemStatus.Cancelled)
            {
                continue;
            }

            var toWaste = choice is { } c ? c == StockDisposition.Waste : started;
            var target = toWaste ? waste : restock;
            target[item.MenuItemId] = target.GetValueOrDefault(item.MenuItemId) + item.Quantity;
        }

        var changed = false;
        foreach (var (id, quantity) in restock)
        {
            var rows = await _db.MenuItems
                .Where(m => m.Id == id && m.StockMode != StockTrackingMode.None && m.StockRemaining != null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.IsAvailable, m => m.IsAvailable || m.StockRemaining == 0)
                    .SetProperty(m => m.StockRemaining, m => m.StockRemaining + quantity), cancellationToken);
            if (rows > 0)
            {
                changed = true;
                var after = await _db.MenuItems.AsNoTracking().Where(m => m.Id == id).Select(m => m.StockRemaining ?? 0).FirstAsync(cancellationToken);
                Record(id, StockMovementType.OrderRelease, after - quantity, quantity, after, orderId, $"Returned to stock: {reason}");
            }
        }

        foreach (var (id, quantity) in waste)
        {
            var item = await _db.MenuItems.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
            if (item is null || item.StockMode == StockTrackingMode.None || item.StockRemaining is null)
            {
                continue;
            }

            var now = item.StockRemaining.Value;
            Record(id, StockMovementType.OrderRelease, now, quantity, now + quantity, orderId, "Order voided or refunded");
            Record(id, StockMovementType.Waste, now + quantity, -quantity, now, orderId, $"Written off: {reason}");
        }

        if (changed)
        {
            await PublishMenuChangeAsync(cancellationToken);
        }
    }

    // One ledger row, saved with the order's own transaction.
    private void Record(int menuItemId, StockMovementType type, int previous, int changed, int now, int? orderId, string? remarks)
    {
        var movement = new StockMovement(menuItemId, type, previous, changed, now, orderId, remarks);
        _db.StockMovements.Add(movement);
        if (orderId is null)
        {
            _unlinked.Add(movement);
        }
    }

    public void LinkPending(Order order)
    {
        foreach (var movement in _unlinked)
        {
            movement.LinkOrder(order);
        }

        _unlinked.Clear();
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
