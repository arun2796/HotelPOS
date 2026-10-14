using System.Text.Json;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Features.Menu;
using HotelPOS.Contracts.Admin;
using HotelPOS.Domain.Administration;
using HotelPOS.Domain.Customers;
using HotelPOS.Domain.Menu;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Import;

// Writes a validated plan and undoes the latest batch. Runs inside the command's transaction; every row it creates or
// changes is recorded on the batch (with the values before the change) so that Undo can put things back.
internal sealed class ImportExecutor
{
    private const string TCategory = "Category";
    private const string TTax = "Tax";
    private const string TStation = "Station";
    private const string TGroup = "ModifierGroup";
    private const string TOption = "ModifierOption";
    private const string TItem = "MenuItem";
    private const string TCustomer = "Customer";

    private static readonly JsonSerializerOptions Json = new();

    private readonly IAppDbContext _db;
    private readonly MenuChanges _menuChanges;

    public ImportExecutor(IAppDbContext db, MenuChanges menuChanges)
    {
        _db = db;
        _menuChanges = menuChanges;
    }

    private sealed record ActiveBefore(bool IsActive);

    private sealed record OptionBefore(decimal PriceDelta, bool IsActive);

    private sealed record ItemBefore(
        int CategoryId, string Name, string? Code, string? Description, decimal Price, int? TaxId, int StationId, int SortOrder, string? HsnSac, bool IsActive, int[] GroupIds);

    private sealed record CustomerBefore(string Name, string? Phone, string? Email, string? Gstin, string? LegalName, string? StateCode, string? Address, bool IsActive);

    public async Task ApplyMenuAsync(ImportBatch batch, IReadOnlyList<MenuRowData> rows, CancellationToken cancellationToken)
    {
        var categories = (await _db.Categories.ToListAsync(cancellationToken)).GroupBy(c => c.Name.ToUpperInvariant()).ToDictionary(g => g.Key, g => g.First());
        var taxes = await _db.Taxes.ToListAsync(cancellationToken);
        var stations = await _db.PreparationStations.ToListAsync(cancellationToken);
        var groups = (await _db.ModifierGroups.Include(g => g.Options).ToListAsync(cancellationToken)).ToDictionary(g => g.Name.ToUpperInvariant(), g => g);
        var defaultStation = stations.Where(s => s.IsActive).OrderBy(s => s.SortOrder).ThenBy(s => s.Id).FirstOrDefault();

        // Categories, taxes and stations the rows name.
        var newCategories = new List<Category>();
        var reactivated = new List<Category>();
        foreach (var name in rows.Select(r => r.Category).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!categories.TryGetValue(name.ToUpperInvariant(), out var category))
            {
                category = new Category(name, (categories.Count + 1) * 10);
                categories[name.ToUpperInvariant()] = category;
                newCategories.Add(category);
                _db.Categories.Add(category);
            }
            else if (!category.IsActive)
            {
                reactivated.Add(category);
            }
        }

        var newTaxes = new List<Tax>();
        foreach (var rate in rows.Where(r => r.TaxRate is not null).Select(r => r.TaxRate!.Value).Distinct())
        {
            if (taxes.All(t => t.RatePercent != rate))
            {
                var label = rate.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                var code = ("GST" + label.Replace('.', '_'));
                while (taxes.Any(t => t.Code == code) || newTaxes.Any(t => t.Code == code))
                {
                    code += "X";
                }

                var tax = new Tax($"GST {label}%", code, rate);
                newTaxes.Add(tax);
                taxes.Add(tax);
                _db.Taxes.Add(tax);
            }
        }

        var newStations = new List<PreparationStation>();
        foreach (var name in rows.Where(r => r.Station is not null).Select(r => r.Station!).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!stations.Any(s => Same(s.Name, name) || Same(s.Code, name)))
            {
                var code = new string(name.ToUpperInvariant().Where(char.IsAsciiLetterOrDigit).Take(10).ToArray());
                code = code.Length == 0 ? "STN" : code;
                while (stations.Any(s => s.Code == code))
                {
                    code = (code.Length > 17 ? code[..17] : code) + "X";
                }

                var station = new PreparationStation(name, code, (stations.Count + 1) * 10);
                newStations.Add(station);
                stations.Add(station);
                _db.PreparationStations.Add(station);
            }
        }

        // Modifier groups: every option a group gets across the rows.
        var wanted = new Dictionary<string, (string Name, Dictionary<string, ModifierOptionSpec> Options)>();
        foreach (var group in rows.SelectMany(r => r.Modifiers))
        {
            var key = group.Name.ToUpperInvariant();
            if (!wanted.TryGetValue(key, out var entry))
            {
                entry = (group.Name, new Dictionary<string, ModifierOptionSpec>());
                wanted[key] = entry;
            }

            foreach (var option in group.Options)
            {
                entry.Options[option.Name.ToUpperInvariant()] = option;
            }
        }

        var newGroups = new List<ModifierGroup>();
        foreach (var (key, entry) in wanted)
        {
            if (!groups.ContainsKey(key))
            {
                var group = new ModifierGroup(entry.Name, 0, Math.Max(1, entry.Options.Count));
                groups[key] = group;
                newGroups.Add(group);
                _db.ModifierGroups.Add(group);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        foreach (var category in reactivated)
        {
            batch.Add(TCategory, category.Id, ImportAction.Updated, Serialize(new ActiveBefore(false)));
            category.Activate();
        }

        foreach (var category in newCategories) { batch.Add(TCategory, category.Id, ImportAction.Created, null); }
        foreach (var tax in newTaxes) { batch.Add(TTax, tax.Id, ImportAction.Created, null); }
        foreach (var station in newStations) { batch.Add(TStation, station.Id, ImportAction.Created, null); }
        foreach (var group in newGroups) { batch.Add(TGroup, group.Id, ImportAction.Created, null); }

        var newOptions = new List<ModifierOption>();
        foreach (var (key, entry) in wanted)
        {
            var group = groups[key];
            var order = group.Options.Count;
            foreach (var spec in entry.Options.Values)
            {
                var existing = group.Options.FirstOrDefault(o => Same(o.Name, spec.Name));
                if (existing is null)
                {
                    var option = new ModifierOption(group.Id, spec.Name, spec.Delta, ++order * 10);
                    newOptions.Add(option);
                    _db.ModifierOptions.Add(option);
                }
                else if (existing.PriceDelta != spec.Delta || !existing.IsActive)
                {
                    batch.Add(TOption, existing.Id, ImportAction.Updated, Serialize(new OptionBefore(existing.PriceDelta, existing.IsActive)));
                    existing.Update(existing.Name, spec.Delta, existing.SortOrder);
                    existing.Activate();
                }
            }
        }

        // Items.
        var categoryIds = categories.Values.Select(c => c.Id).ToList();
        var items = await _db.MenuItems.Include(i => i.ModifierGroups).Where(i => categoryIds.Contains(i.CategoryId)).ToListAsync(cancellationToken);
        var itemsByKey = items.GroupBy(i => (i.CategoryId, i.Name.ToUpperInvariant())).ToDictionary(g => g.Key, g => g.First());
        var createdItems = new List<MenuItem>();
        var sort = 0;
        foreach (var row in rows)
        {
            sort++;
            var category = categories[row.Category.ToUpperInvariant()];
            var tax = row.TaxRate is { } rate
                ? taxes.First(t => t.RatePercent == rate)
                : row.TaxName is { } taxName ? taxes.First(t => Same(t.Name, taxName)) : null;
            var station = row.Station is null
                ? defaultStation!
                : stations.First(s => Same(s.Name, row.Station) || Same(s.Code, row.Station));
            var groupIds = row.Modifiers.Select(m => groups[m.Name.ToUpperInvariant()].Id).ToList();

            if (itemsByKey.TryGetValue((category.Id, row.Item.ToUpperInvariant()), out var item))
            {
                batch.Add(TItem, item.Id, ImportAction.Updated, Serialize(new ItemBefore(
                    item.CategoryId, item.Name, item.Code, item.Description, item.Price, item.TaxId, item.PreparationStationId,
                    item.SortOrder, item.HsnSac, item.IsActive, item.ModifierGroups.OrderBy(l => l.SortOrder).Select(l => l.ModifierGroupId).ToArray())));
                item.Update(category.Id, item.Name, row.Code ?? item.Code, row.Description ?? item.Description, row.Price, tax?.Id ?? item.TaxId, station.Id, item.SortOrder);
                if (row.Hsn is not null) { item.SetHsnSac(row.Hsn); }
                if (groupIds.Count > 0) { item.SetModifierGroups(groupIds); }
                item.Activate();
            }
            else
            {
                item = new MenuItem(category.Id, row.Item, row.Code, row.Description, row.Price, tax?.Id, station.Id, Math.Min(sort, 9999));
                item.SetHsnSac(row.Hsn);
                item.SetModifierGroups(groupIds);
                createdItems.Add(item);
                _db.MenuItems.Add(item);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        foreach (var option in newOptions) { batch.Add(TOption, option.Id, ImportAction.Created, null); }
        foreach (var item in createdItems) { batch.Add(TItem, item.Id, ImportAction.Created, null); }
        await _menuChanges.SaveAsync(cancellationToken);
    }

    public async Task ApplyCustomersAsync(ImportBatch batch, IReadOnlyList<CustomerRowData> rows, CancellationToken cancellationToken)
    {
        var existing = await _db.Customers.ToListAsync(cancellationToken);
        var created = new List<Customer>();
        foreach (var row in rows)
        {
            var customer = existing.FirstOrDefault(c => row.Phone is not null ? c.Phone == row.Phone
                : row.Gstin is not null ? c.Gstin == row.Gstin
                : Same(c.Name, row.Name));
            if (customer is null)
            {
                customer = new Customer(row.Name, row.Phone, row.Email, row.Gstin, row.LegalName, row.StateCode, row.Address);
                existing.Add(customer);
                created.Add(customer);
                _db.Customers.Add(customer);
            }
            else
            {
                batch.Add(TCustomer, customer.Id, ImportAction.Updated, Serialize(new CustomerBefore(
                    customer.Name, customer.Phone, customer.Email, customer.Gstin, customer.LegalName, customer.StateCode, customer.Address, customer.IsActive)));
                customer.Update(row.Name, row.Phone ?? customer.Phone, row.Email ?? customer.Email, row.Gstin ?? customer.Gstin,
                    row.LegalName ?? customer.LegalName, row.StateCode ?? customer.StateCode, row.Address ?? customer.Address);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        foreach (var customer in created) { batch.Add(TCustomer, customer.Id, ImportAction.Created, null); }
    }

    public async Task UndoAsync(ImportBatch batch, CancellationToken cancellationToken)
    {
        var previous = string.Empty;
        foreach (var entry in batch.Entries.OrderByDescending(e => e.Seq))
        {
            // Rows of one type are undone together; later queries must see the deletes of the earlier type.
            if (entry.EntityType != previous)
            {
                await _db.SaveChangesAsync(cancellationToken);
                previous = entry.EntityType;
            }

            if (entry.Action == ImportAction.Created)
            {
                await RemoveAsync(entry, cancellationToken);
            }
            else
            {
                await RestoreAsync(entry, cancellationToken);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        if (batch.Kind == ImportKind.Menu)
        {
            await _menuChanges.SaveAsync(cancellationToken);
        }
    }

    private async Task RemoveAsync(ImportEntry entry, CancellationToken cancellationToken)
    {
        var id = entry.EntityId;
        switch (entry.EntityType)
        {
            case TItem:
                if (await _db.MenuItems.FirstOrDefaultAsync(i => i.Id == id, cancellationToken) is { } item)
                {
                    if (await _db.OrderItems.AnyAsync(o => o.MenuItemId == id, cancellationToken)) { item.Deactivate(); } else { _db.MenuItems.Remove(item); }
                }

                break;
            case TOption:
                if (await _db.ModifierOptions.FirstOrDefaultAsync(o => o.Id == id, cancellationToken) is { } option)
                {
                    if (await _db.OrderItems.AnyAsync(o => o.Modifiers.Any(m => m.ModifierOptionId == id), cancellationToken)) { option.Deactivate(); } else { _db.ModifierOptions.Remove(option); }
                }

                break;
            case TGroup:
                if (await _db.ModifierGroups.Include(g => g.Options).FirstOrDefaultAsync(g => g.Id == id, cancellationToken) is { } group)
                {
                    if (await _db.MenuItemModifierGroups.AnyAsync(l => l.ModifierGroupId == id, cancellationToken) || group.Options.Count > 0) { group.Deactivate(); } else { _db.ModifierGroups.Remove(group); }
                }

                break;
            case TStation:
                if (await _db.PreparationStations.FirstOrDefaultAsync(s => s.Id == id, cancellationToken) is { } station)
                {
                    if (await _db.MenuItems.AnyAsync(i => i.PreparationStationId == id, cancellationToken)
                        || await _db.Devices.AnyAsync(d => d.StationId == id, cancellationToken)
                        || await _db.KitchenOrders.AnyAsync(k => k.PreparationStationId == id, cancellationToken)) { station.Deactivate(); } else { _db.PreparationStations.Remove(station); }
                }

                break;
            case TTax:
                if (await _db.Taxes.FirstOrDefaultAsync(t => t.Id == id, cancellationToken) is { } tax)
                {
                    if (await _db.MenuItems.AnyAsync(i => i.TaxId == id, cancellationToken)) { tax.Deactivate(); } else { _db.Taxes.Remove(tax); }
                }

                break;
            case TCategory:
                if (await _db.Categories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken) is { } category)
                {
                    if (await _db.MenuItems.AnyAsync(i => i.CategoryId == id, cancellationToken)) { category.Deactivate(); } else { _db.Categories.Remove(category); }
                }

                break;
            case TCustomer:
                if (await _db.Customers.FirstOrDefaultAsync(c => c.Id == id, cancellationToken) is { } customer)
                {
                    _db.Customers.Remove(customer);
                }

                break;
        }
    }

    private async Task RestoreAsync(ImportEntry entry, CancellationToken cancellationToken)
    {
        var id = entry.EntityId;
        switch (entry.EntityType)
        {
            case TCategory when await _db.Categories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken) is { } category:
                if (!Deserialize<ActiveBefore>(entry).IsActive) { category.Deactivate(); }
                break;
            case TOption when await _db.ModifierOptions.FirstOrDefaultAsync(o => o.Id == id, cancellationToken) is { } option:
                var before = Deserialize<OptionBefore>(entry);
                option.Update(option.Name, before.PriceDelta, option.SortOrder);
                if (before.IsActive) { option.Activate(); } else { option.Deactivate(); }
                break;
            case TItem when await _db.MenuItems.Include(i => i.ModifierGroups).FirstOrDefaultAsync(i => i.Id == id, cancellationToken) is { } item:
                var b = Deserialize<ItemBefore>(entry);
                item.Update(b.CategoryId, b.Name, b.Code, b.Description, b.Price, b.TaxId, b.StationId, b.SortOrder);
                item.SetHsnSac(b.HsnSac);
                item.SetModifierGroups(b.GroupIds);
                if (b.IsActive) { item.Activate(); } else { item.Deactivate(); }
                break;
            case TCustomer when await _db.Customers.FirstOrDefaultAsync(c => c.Id == id, cancellationToken) is { } customer:
                var c = Deserialize<CustomerBefore>(entry);
                customer.Update(c.Name, c.Phone, c.Email, c.Gstin, c.LegalName, c.StateCode, c.Address);
                break;
        }
    }

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);

    private static T Deserialize<T>(ImportEntry entry) => JsonSerializer.Deserialize<T>(entry.Before!, Json)!;
}
