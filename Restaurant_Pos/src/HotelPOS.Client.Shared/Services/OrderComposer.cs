using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Orders;

namespace HotelPOS.Client.Services;

public sealed class CartLine
{
    public int MenuItemId { get; init; }

    public string Name { get; init; } = string.Empty;

    public decimal UnitPrice { get; set; }

    /// <summary>The portion ordered ("Half"). PortionId is sent to the server; PortionName is set only for an item that has more than one
    /// portion on sale (an item with one price shows no portion). Quantity and unit are labels.</summary>
    public int? PortionId { get; set; }

    public string? PortionName { get; set; }

    public decimal? PortionQuantity { get; set; }

    public string? PortionUnit { get; set; }

    /// <summary>A loaded draft line whose portion is no longer on the menu: it must be chosen again before the order is sent.</summary>
    public bool NeedsPortionChoice { get; set; }

    /// <summary>"Chicken Biryani (Half)": the dish as staff read it.</summary>
    public string DisplayName => ItemDisplayName.Compose(Name, PortionName);

    public int Quantity { get; set; } = 1;

    public string? Notes { get; set; }

    public IReadOnlyList<int> ModifierOptionIds { get; init; } = Array.Empty<int>();

    public IReadOnlyList<string> ModifierNames { get; init; } = Array.Empty<string>();

    public decimal ModifierTotal { get; init; }

    /// <summary>Takeaway packed with a dine-in meal: the packaging charge is added to the line and the KOT says "Parcel".</summary>
    public bool IsParcel { get; set; }

    /// <summary>"4A" = table code + seat letter; null = the whole table.</summary>
    public string? SeatCode { get; set; }

    /// <summary>The item's packaging charge per unit, added to <see cref="LineTotal"/> only while <see cref="IsParcel"/> is on.</summary>
    public decimal PackagingCharge { get; set; }

    public decimal LineTotal => (UnitPrice + ModifierTotal + (IsParcel ? PackagingCharge : 0m)) * Quantity;

    public string DetailText => string.Join(", ", ModifierNames.Concat(string.IsNullOrWhiteSpace(Notes) ? Array.Empty<string>() : new[] { $"“{Notes}”" }));

    /// <summary>Two lines merge when item, portion, options, note, parcel flag and seat are the same — the desktop Order Builder does the same.</summary>
    public string MergeKey => $"{MenuItemId}|{string.Join(",", ModifierOptionIds.OrderBy(i => i))}|{Notes}|{(IsParcel ? "P" : "-")}|{SeatCode}|{PortionId}";
}

/// <summary>
/// The cart of an order being built, with the same rules as the desktop Order Builder: quantities 1–99 per line,
/// guests 1–99, notes up to 500 characters, option groups satisfied between Min and Max selections. Pure logic so it
/// can be unit-tested without a browser.
/// </summary>
public sealed class OrderComposer
{
    public static IReadOnlyList<string> QuickNotes { get; } = new[]
    {
        "Less spicy", "Extra spicy", "No onion", "No garlic", "Less oil", "Jain", "Parcel",
    };

    private readonly List<CartLine> _lines = new();
    private int _guests = 1;
    private string? _notes;

    public IReadOnlyList<CartLine> Lines => _lines;

    public int Guests
    {
        get => _guests;
        set => _guests = Math.Clamp(value, FloorLimits.MinGuests, FloorLimits.MaxGuests);
    }

    /// <summary>Order-level note for the kitchen (CreateOrderRequest.Notes, 500 characters); null when blank.</summary>
    public string? Notes
    {
        get => _notes;
        set => _notes = TrimNotes(value);
    }

    public int ItemCount => _lines.Sum(l => l.Quantity);

    public decimal ApproxTotal => _lines.Sum(l => l.LineTotal);

    public bool IsEmpty => _lines.Count == 0;

    // portion: the one chosen on the item card; none means the item's default portion (an item from a server that sends no portions
    // is priced from the item, as before).
    public void Add(MenuEntryDto item, IReadOnlyList<MenuModifierOptionDto> options, int quantity, string? notes, bool isParcel = false, string? seatCode = null, MenuItemPortionDto? portion = null)
    {
        var chosen = portion ?? DefaultPortion(item);
        var named = item.Portions.Count > 1 ? chosen : null;
        var line = new CartLine
        {
            MenuItemId = item.Id,
            Name = item.Name,
            UnitPrice = chosen?.Price ?? item.Price,
            PortionId = chosen?.Id,
            PortionName = named?.Name,
            PortionQuantity = named?.Quantity,
            PortionUnit = named?.Unit,
            Quantity = Math.Clamp(quantity, 1, OrderLimits.MaxQuantity),
            Notes = TrimNotes(notes),
            ModifierOptionIds = options.Select(o => o.Id).ToList(),
            ModifierNames = options.Select(o => o.Name).ToList(),
            ModifierTotal = options.Sum(o => o.PriceDelta),
            IsParcel = isParcel,
            SeatCode = string.IsNullOrWhiteSpace(seatCode) ? null : seatCode.Trim(),
            PackagingCharge = item.PackagingCharge,
        };

        var existing = _lines.FirstOrDefault(l => l.MergeKey == line.MergeKey);
        if (existing is not null)
        {
            existing.Quantity = Math.Min(existing.Quantity + line.Quantity, OrderLimits.MaxQuantity);
            return;
        }

        _lines.Add(line);
    }

    public void Increase(CartLine line) => line.Quantity = Math.Min(line.Quantity + 1, OrderLimits.MaxQuantity);

    public void Decrease(CartLine line)
    {
        if (line.Quantity <= 1)
        {
            _lines.Remove(line);
            return;
        }

        line.Quantity--;
    }

    /// <summary>The portion an item is sold in by default: the one marked default, else the first one on sale; null when the server sends none.</summary>
    public static MenuItemPortionDto? DefaultPortion(MenuEntryDto item) =>
        item.Portions.FirstOrDefault(p => p.IsDefault) ?? item.Portions.FirstOrDefault();

    /// <summary>True while a loaded draft line still has to be given a portion (its portion is no longer on the menu).</summary>
    public bool HasUnresolvedPortions => _lines.Any(l => l.NeedsPortionChoice);

    /// <summary>
    /// After a draft or a saved order was loaded: ties each line to a portion of today's menu, by id and then by name. An item with a
    /// single portion needs no choice. A line whose portion is gone, or that never had one on an item with several, is flagged and
    /// is never silently given the default: it has to be chosen again.
    /// </summary>
    public void ResolvePortions(MenuDto menu)
    {
        foreach (var line in _lines)
        {
            var item = menu.Items.FirstOrDefault(i => i.Id == line.MenuItemId);
            if (item is null || item.Portions.Count == 0)
            {
                continue;
            }

            var byId = line.PortionId is { } id ? item.Portions.FirstOrDefault(p => p.Id == id) : null;
            var byName = byId is null && !string.IsNullOrWhiteSpace(line.PortionName)
                ? item.Portions.FirstOrDefault(p => string.Equals(p.Name, line.PortionName, StringComparison.OrdinalIgnoreCase))
                : null;
            var found = byId ?? byName;
            if (found is not null)
            {
                line.PortionId = found.Id;
                line.NeedsPortionChoice = false;
            }
            else if (line.PortionId is null && string.IsNullOrWhiteSpace(line.PortionName) && item.Portions.Count == 1)
            {
                line.PortionId = item.Portions[0].Id;
                line.NeedsPortionChoice = false;
            }
            else
            {
                line.NeedsPortionChoice = true;
            }
        }
    }

    /// <summary>Gives a line (usually one flagged by <see cref="ResolvePortions"/>) the portion just picked; its price follows the portion.</summary>
    public void ChoosePortion(CartLine line, MenuEntryDto item, MenuItemPortionDto portion)
    {
        var named = item.Portions.Count > 1;
        line.UnitPrice = portion.Price;
        line.PortionId = portion.Id;
        line.PortionName = named ? portion.Name : null;
        line.PortionQuantity = named ? portion.Quantity : null;
        line.PortionUnit = named ? portion.Unit : null;
        line.NeedsPortionChoice = false;
        MergeInto(line);
    }

    public void Remove(CartLine line) => _lines.Remove(line);

    public void SetNotes(CartLine line, string? notes) => line.Notes = TrimNotes(notes);

    public void SetQuantity(CartLine line, int quantity) => line.Quantity = Math.Clamp(quantity, 1, OrderLimits.MaxQuantity);

    /// <summary>
    /// Turns the parcel flag of one line on or off. <paramref name="packagingCharge"/> (the menu's current charge) replaces the
    /// stored one when given. The line then merges into an identical line if there is one.
    /// </summary>
    public void SetParcel(CartLine line, bool parcel, decimal? packagingCharge = null)
    {
        line.IsParcel = parcel;
        if (packagingCharge is { } charge)
        {
            line.PackagingCharge = charge;
        }

        MergeInto(line);
    }

    /// <summary>Drops every seat (the order is no longer for a table, or the table has no seats); identical lines then merge.</summary>
    public void ClearSeats()
    {
        foreach (var line in _lines.ToList())
        {
            if (line.SeatCode is not null)
            {
                line.SeatCode = null;
                MergeInto(line);
            }
        }
    }

    // After a flag changed: fold the line into an earlier identical one (quantities add up to the per-line maximum).
    private void MergeInto(CartLine line)
    {
        var twin = _lines.FirstOrDefault(l => !ReferenceEquals(l, line) && l.MergeKey == line.MergeKey);
        if (twin is null)
        {
            return;
        }

        twin.Quantity = Math.Min(twin.Quantity + line.Quantity, OrderLimits.MaxQuantity);
        _lines.Remove(line);
    }

    public void Clear()
    {
        _lines.Clear();
        _notes = null;
    }

    public IReadOnlyList<OrderItemInput> ToInputs() =>
        _lines.Select(l => new OrderItemInput
        {
            MenuItemId = l.MenuItemId,
            Quantity = l.Quantity,
            Notes = l.Notes,
            ModifierOptionIds = l.ModifierOptionIds,
            IsParcel = l.IsParcel,
            SeatCode = l.SeatCode,
            PortionId = l.PortionId,
        }).ToList();

    public WebDraft ToDraft(int tableId, int? orderId, Guid? pendingKey) => new()
    {
        TableId = tableId,
        OrderId = orderId,
        Guests = Guests,
        Notes = Notes,
        PendingKey = pendingKey,
        Lines = _lines.Select(l => new WebDraftLine
        {
            MenuItemId = l.MenuItemId,
            Name = l.Name,
            UnitPrice = l.UnitPrice,
            Quantity = l.Quantity,
            Notes = l.Notes,
            ModifierOptionIds = l.ModifierOptionIds,
            ModifierNames = l.ModifierNames,
            ModifierTotal = l.ModifierTotal,
            IsParcel = l.IsParcel,
            SeatCode = l.SeatCode,
            PackagingCharge = l.PackagingCharge,
            PortionId = l.PortionId,
            PortionName = l.PortionName,
            PortionQuantity = l.PortionQuantity,
            PortionUnit = l.PortionUnit,
        }).ToList(),
    };

    public void LoadDraft(WebDraft draft)
    {
        Guests = draft.Guests;
        Notes = draft.Notes;
        _lines.Clear();
        _lines.AddRange(draft.Lines.Select(l => new CartLine
        {
            MenuItemId = l.MenuItemId,
            Name = l.Name,
            UnitPrice = l.UnitPrice,
            Quantity = Math.Clamp(l.Quantity, 1, OrderLimits.MaxQuantity),
            Notes = l.Notes,
            ModifierOptionIds = l.ModifierOptionIds,
            ModifierNames = l.ModifierNames,
            ModifierTotal = l.ModifierTotal,
            IsParcel = l.IsParcel,
            SeatCode = l.SeatCode,
            PackagingCharge = l.PackagingCharge,
            PortionId = l.PortionId,
            PortionName = l.PortionName,
            PortionQuantity = l.PortionQuantity,
            PortionUnit = l.PortionUnit,
        }));
    }

    public void LoadOrder(OrderDetailDto order)
    {
        Guests = order.GuestCount;
        Notes = order.Notes;
        _lines.Clear();
        _lines.AddRange(order.Items.Where(i => i.Status != OrderItemStatus.Cancelled).Select(i => new CartLine
        {
            MenuItemId = i.MenuItemId,
            // The server folds the packaging charge into UnitPrice and appends " (Parcel)" to the name; undo both so the
            // cart shows the dish and adds the charge itself (a re-send would otherwise charge and label it twice).
            Name = i.IsParcel ? StripParcelSuffix(i.ItemName) : i.ItemName,
            UnitPrice = i.IsParcel ? i.UnitPrice - i.PackagingCharge : i.UnitPrice,
            Quantity = Math.Clamp(i.Quantity, 1, OrderLimits.MaxQuantity),
            Notes = i.Notes,
            ModifierOptionIds = i.Modifiers.Select(m => m.ModifierOptionId).ToList(),
            ModifierNames = i.Modifiers.Select(m => m.Name).ToList(),
            ModifierTotal = i.Modifiers.Sum(m => m.PriceDelta),
            IsParcel = i.IsParcel,
            SeatCode = string.IsNullOrWhiteSpace(i.SeatCode) ? null : i.SeatCode,
            PackagingCharge = i.PackagingCharge,
            PortionId = i.PortionId,
            PortionName = i.PortionName,
            PortionQuantity = i.PortionQuantity,
            PortionUnit = i.PortionUnit,
        }));
    }

    public const string ParcelSuffix = " (Parcel)";

    /// <summary>"Dal Makhani (Parcel)" becomes "Dal Makhani"; any other name is returned as it is.</summary>
    public static string StripParcelSuffix(string name) =>
        name.EndsWith(ParcelSuffix, StringComparison.Ordinal) ? name[..^ParcelSuffix.Length] : name;

    public static string? TrimNotes(string? notes)
    {
        var trimmed = notes?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= OrderLimits.NotesMaxLength ? trimmed : trimmed[..OrderLimits.NotesMaxLength];
    }

    /// <summary>"Choose 1", "Optional · up to 3", "Choose 1 to 2" — the wording of the desktop options pop-up.</summary>
    public static string GroupRuleText(MenuModifierGroupDto group) => (group.MinSelections, group.MaxSelections) switch
    {
        (0, 1) => "Optional",
        (0, var max) => $"Optional · up to {max}",
        (var min, var max) when min == max => $"Choose {min}",
        (var min, var max) => $"Choose {min} to {max}",
    };

    public static bool GroupSatisfied(MenuModifierGroupDto group, int selected) =>
        selected >= group.MinSelections && selected <= group.MaxSelections;

    public static bool CanSelectMore(MenuModifierGroupDto group, int selected) => selected < group.MaxSelections;

    public static bool AllGroupsSatisfied(IEnumerable<MenuModifierGroupDto> groups, Func<MenuModifierGroupDto, int> selectedCount) =>
        groups.All(g => GroupSatisfied(g, selectedCount(g)));
}
