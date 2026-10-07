using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Orders;
using HotelPOS.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Orders;

public sealed class OrderItemFactory
{
    private readonly IAppDbContext _db;

    public OrderItemFactory(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<Result<List<OrderItem>>> CreateAsync(IReadOnlyList<OrderItemInput> inputs, CancellationToken cancellationToken)
    {
        var menuItemIds = inputs.Select(i => i.MenuItemId).Distinct().ToList();
        var menuItems = await _db.MenuItems.AsNoTracking()
            .Include(m => m.Category)
            .Include(m => m.Tax)
            .Include(m => m.ModifierGroups)
            .Where(m => menuItemIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, cancellationToken);

        var groupIds = menuItems.Values.SelectMany(m => m.ModifierGroups.Select(l => l.ModifierGroupId)).Distinct().ToList();
        var groups = await _db.ModifierGroups.AsNoTracking()
            .Include(g => g.Options)
            .Where(g => groupIds.Contains(g.Id) && g.IsActive)
            .ToDictionaryAsync(g => g.Id, cancellationToken);

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

            result.Add(new OrderItem(
                menuItem.Id,
                menuItem.Name,
                menuItem.Price,
                input.Quantity,
                input.Notes,
                menuItem.TaxId,
                menuItem.Tax?.RatePercent ?? 0m,
                menuItem.PreparationStationId,
                chosen.Select(id => new OrderItemModifier(id, options[id].Name, options[id].PriceDelta))));
        }

        return result;
    }
}
