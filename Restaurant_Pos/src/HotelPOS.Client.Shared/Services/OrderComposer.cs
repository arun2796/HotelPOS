using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Orders;

namespace HotelPOS.Client.Services;

public sealed class CartLine
{
    public int MenuItemId { get; init; }

    public string Name { get; init; } = string.Empty;

    public decimal UnitPrice { get; init; }

    public int Quantity { get; set; } = 1;

    public string? Notes { get; set; }

    public IReadOnlyList<int> ModifierOptionIds { get; init; } = Array.Empty<int>();

    public IReadOnlyList<string> ModifierNames { get; init; } = Array.Empty<string>();

    public decimal ModifierTotal { get; init; }

    public decimal LineTotal => (UnitPrice + ModifierTotal) * Quantity;

    public string DetailText => string.Join(", ", ModifierNames.Concat(string.IsNullOrWhiteSpace(Notes) ? Array.Empty<string>() : new[] { $"“{Notes}”" }));

    /// <summary>Two lines merge when item, options and note are the same — the desktop Order Builder does the same.</summary>
    public string MergeKey => $"{MenuItemId}|{string.Join(",", ModifierOptionIds.OrderBy(i => i))}|{Notes}";
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

    public void Add(MenuEntryDto item, IReadOnlyList<MenuModifierOptionDto> options, int quantity, string? notes)
    {
        var line = new CartLine
        {
            MenuItemId = item.Id,
            Name = item.Name,
            UnitPrice = item.Price,
            Quantity = Math.Clamp(quantity, 1, OrderLimits.MaxQuantity),
            Notes = TrimNotes(notes),
            ModifierOptionIds = options.Select(o => o.Id).ToList(),
            ModifierNames = options.Select(o => o.Name).ToList(),
            ModifierTotal = options.Sum(o => o.PriceDelta),
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

    public void Remove(CartLine line) => _lines.Remove(line);

    public void SetNotes(CartLine line, string? notes) => line.Notes = TrimNotes(notes);

    public void SetQuantity(CartLine line, int quantity) => line.Quantity = Math.Clamp(quantity, 1, OrderLimits.MaxQuantity);

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
            Name = i.ItemName,
            UnitPrice = i.UnitPrice,
            Quantity = Math.Clamp(i.Quantity, 1, OrderLimits.MaxQuantity),
            Notes = i.Notes,
            ModifierOptionIds = i.Modifiers.Select(m => m.ModifierOptionId).ToList(),
            ModifierNames = i.Modifiers.Select(m => m.Name).ToList(),
            ModifierTotal = i.Modifiers.Sum(m => m.PriceDelta),
        }));
    }

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
