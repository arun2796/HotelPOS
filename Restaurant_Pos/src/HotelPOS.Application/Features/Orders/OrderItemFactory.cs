using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Orders;
using HotelPOS.Domain.Floor;
using HotelPOS.Domain.Menu;
using HotelPOS.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Orders;

internal sealed class OrderItemFactory
{
    private readonly IAppDbContext _db;

    public OrderItemFactory(IAppDbContext db)
    {
        _db = db;
    }

    public Task<Result<List<OrderItem>>> CreateAsync(IReadOnlyList<OrderItemInput> inputs, CancellationToken cancellationToken) =>
        CreateAsync(inputs, null, cancellationToken);

    // existingSeatGroups: the seat groups the order already has (dishes added later), so a seat cannot end up in two groups.
    public async Task<Result<List<OrderItem>>> CreateAsync(IReadOnlyList<OrderItemInput> inputs, int? tableId, IEnumerable<string?> existingSeatGroups, CancellationToken cancellationToken)
    {
        var created = await CreateAsync(inputs, tableId, cancellationToken);
        if (created.IsFailure)
        {
            return created;
        }

        var conflict = SeatSelection.FindConflict(existingSeatGroups.Concat(created.Value.Select(i => i.SeatCode)));
        return conflict is null ? created : AppErrors.Validation("items", conflict);
    }

    // tableId: the order's table, so seat codes ("4A") can be checked against it and the tables merged into it.
    public async Task<Result<List<OrderItem>>> CreateAsync(IReadOnlyList<OrderItemInput> inputs, int? tableId, CancellationToken cancellationToken)
    {
        IReadOnlyList<Table> seatTables = Array.Empty<Table>();
        if (tableId is { } seatTableId && inputs.Any(i => !string.IsNullOrWhiteSpace(i.SeatCode)))
        {
            seatTables = await _db.Tables.AsNoTracking()
                .Where(t => t.Id == seatTableId || t.MergedIntoTableId == seatTableId)
                .ToListAsync(cancellationToken);
        }

        var menuItemIds = inputs.Select(i => i.MenuItemId).Distinct().ToList();
        var menuItems = await _db.MenuItems.AsNoTracking()
            .Include(m => m.Category)
            .Include(m => m.Tax)
            .Include(m => m.ModifierGroups)
            .Include(m => m.Portions)
            .Where(m => menuItemIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, cancellationToken);

        var groupIds = menuItems.Values.SelectMany(m => m.ModifierGroups.Select(l => l.ModifierGroupId)).Distinct().ToList();
        var groups = await _db.ModifierGroups.AsNoTracking()
            .Include(g => g.Options)
            .Where(g => groupIds.Contains(g.Id) && g.IsActive)
            .ToDictionaryAsync(g => g.Id, cancellationToken);

        // Parcel items with no packing charge of their own pay the restaurant-wide one (Admin > Settings).
        var defaultPackaging = inputs.Any(i => i.IsParcel)
            ? await _db.DecimalSettingAsync(SettingKeys.DefaultPackagingCharge, 0m, cancellationToken)
            : 0m;

        var result = new List<OrderItem>();
        for (var index = 0; index < inputs.Count; index++)
        {
            var input = inputs[index];
            var field = $"items[{index}]";
            if (!menuItems.TryGetValue(input.MenuItemId, out var menuItem) || !menuItem.IsActive || menuItem.Category?.IsActive != true)
            {
                return AppErrors.Validation(field, "This item is no longer on the menu. Refresh the menu and try again.");
            }

            if (!menuItem.IsAvailable)
            {
                return AppErrors.BusinessRule($"{menuItem.Name} is sold out.");
            }

            string? seatCode = null;
            if (!string.IsNullOrWhiteSpace(input.SeatCode))
            {
                var chosenSeats = SeatSelection.Parse(input.SeatCode);
                seatCode = SeatSelection.Join(chosenSeats);
                if (seatCode.Length > SeatSelection.MaxLength)
                {
                    return AppErrors.Validation(field, "Too many seats are selected together. Choose fewer seats, or leave the seat empty for the whole table.");
                }

                if (seatTables.Count == 0 || chosenSeats.Count == 0 || chosenSeats.Any(s => !TableSeats.IsValid(seatTables, s)))
                {
                    var offered = seatTables.Count == 0 ? "this order has no table" : "its seats are " + string.Join(", ", seatTables.SelectMany(t => TableSeats.CodesFor(t.Code, t.Capacity)));
                    return AppErrors.Validation(field, $"Seat {seatCode} does not exist: {offered}.");
                }
            }

            var linkedGroups = menuItem.ModifierGroups
                .Where(l => groups.ContainsKey(l.ModifierGroupId))
                .Select(l => groups[l.ModifierGroupId])
                .ToList();
            var options = linkedGroups.SelectMany(g => g.Options.Where(o => o.IsActive)).ToDictionary(o => o.Id);

            var chosen = input.ModifierOptionIds.Distinct().ToList();
            if (chosen.Any(id => !options.ContainsKey(id)))
            {
                return AppErrors.Validation(field, $"An option chosen for {menuItem.Name} is not offered with it.");
            }

            foreach (var group in linkedGroups)
            {
                var count = chosen.Count(id => options[id].ModifierGroupId == group.Id);
                if (count < group.MinSelections || count > group.MaxSelections)
                {
                    return AppErrors.Validation(field, group.MinSelections == group.MaxSelections
                        ? $"{menuItem.Name}: choose exactly {group.MinSelections} for {group.Name}."
                        : $"{menuItem.Name}: choose {group.MinSelections} to {group.MaxSelections} for {group.Name}.");
                }
            }

            // The portion the guest chose, else the only one on sale. The price comes from it, never from the client.
            var portion = ResolvePortion(menuItem, input.PortionId, out var portionError);
            if (portionError is not null)
            {
                return AppErrors.Validation(field, portionError);
            }

            // A parcel pays the item's packaging charge. It is folded into the unit price, so the bill, GST and every
            // total follow without a separate line; the name says Parcel on the KOT and the invoice.
            var packaging = !input.IsParcel ? 0m : menuItem.PackagingCharge > 0m ? menuItem.PackagingCharge : defaultPackaging;
            var lineName = input.IsParcel ? ParcelName(menuItem.Name) : menuItem.Name;
            var line = new OrderItem(
                menuItem.Id,
                lineName,
                (portion?.Price ?? menuItem.Price) + packaging,
                input.Quantity,
                input.Notes,
                menuItem.TaxId,
                menuItem.Tax?.RatePercent ?? 0m,
                menuItem.PreparationStationId,
                chosen.Select(id => new OrderItemModifier(id, options[id].Name, options[id].PriceDelta)),
                input.IsParcel,
                packaging,
                seatCode);
            // Only an item that has more than one portion defined keeps the portion on the line: an item with one price
            // reads exactly as it did before portions existed.
            if (portion is not null && menuItem.Portions.Count > 1)
            {
                line.SetPortion(portion.Id, portion.Name, portion.Quantity, portion.Unit);
            }

            result.Add(line);
        }

        var inputConflict = SeatSelection.FindConflict(result.Select(i => i.SeatCode));
        if (inputConflict is not null)
        {
            return AppErrors.Validation("items", inputConflict);
        }

        return result;
    }

    // The portion a line is for. A portion that was named must still be offered by this item and switched on: it is refused
    // (never swapped for the default) when it is gone or off. With no portion named, the only portion on sale is used; two
    // or more on sale is refused, so an app that does not know portions cannot charge the default by accident.
    // An item without portion rows (not possible after the Phase32 migration) is priced from the item, as before.
    private static MenuItemPortion? ResolvePortion(MenuItem item, int? portionId, out string? error)
    {
        error = null;
        if (item.Portions.Count == 0)
        {
            return null;
        }

        if (portionId is { } id)
        {
            var named = item.Portions.FirstOrDefault(p => p.Id == id);
            if (named is null)
            {
                error = $"That portion is not offered with {item.Name}. Refresh the menu and choose again.";
                return null;
            }

            if (!named.IsActive)
            {
                error = $"{item.Name} ({named.Name}) is no longer available. Choose another portion.";
                return null;
            }

            return named;
        }

        var onSale = item.Portions.Where(p => p.IsActive).ToList();
        if (onSale.Count == 1)
        {
            return onSale[0];
        }

        error = onSale.Count == 0
            ? $"{item.Name} has no portion on sale."
            : $"{item.Name} has several portions. Update the app to choose one.";
        return null;
    }

    private static string ParcelName(string name)
    {
        const string suffix = " (Parcel)";
        return (name.Length + suffix.Length > MenuLimits.NameMaxLength ? name[..(MenuLimits.NameMaxLength - suffix.Length)] : name) + suffix;
    }
}
