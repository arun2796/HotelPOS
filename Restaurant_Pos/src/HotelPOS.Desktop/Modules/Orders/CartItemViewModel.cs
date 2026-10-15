using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using HotelPOS.Contracts.Orders;
using HotelPOS.Desktop.Services.Orders;

namespace HotelPOS.Desktop.Modules.Orders;

public sealed partial class CartItemViewModel : ObservableObject
{
    public CartItemViewModel(DraftLine line)
    {
        MenuItemId = line.MenuItemId;
        Name = line.Name;
        UnitPrice = line.UnitPrice;
        ModifierOptionIds = line.ModifierOptionIds;
        ModifierNames = line.ModifierNames;
        ModifierTotal = line.ModifierTotal;
        _quantity = line.Quantity;
        _notes = line.Notes;
    }

    public int MenuItemId { get; }
    public string Name { get; }
    public decimal UnitPrice { get; }
    public IReadOnlyList<int> ModifierOptionIds { get; }
    public IReadOnlyList<string> ModifierNames { get; }
    public decimal ModifierTotal { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LineTotal), nameof(LineTotalText))]
    private int _quantity;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailText), nameof(HasDetail))]
    private string? _notes;

    public decimal LineTotal => (UnitPrice + ModifierTotal) * Quantity;

    public string LineTotalText => LineTotal.ToString("N2", CultureInfo.CurrentCulture);

    public string DetailText => string.Join(" · ", ModifierNames.Concat(string.IsNullOrWhiteSpace(Notes) ? Array.Empty<string>() : new[] { $"“{Notes}”" }));

    public bool HasDetail => DetailText.Length > 0;

    public bool CanIncrease => Quantity < OrderLimits.MaxQuantity;

    public bool IsSameChoice(int menuItemId, IReadOnlyCollection<int> optionIds) =>
        MenuItemId == menuItemId && string.IsNullOrWhiteSpace(Notes) && ModifierOptionIds.Count == optionIds.Count
        && ModifierOptionIds.All(optionIds.Contains);

    public DraftLine ToLine() => new()
    {
        MenuItemId = MenuItemId,
        Name = Name,
        UnitPrice = UnitPrice,
        Quantity = Quantity,
        Notes = Notes,
        ModifierOptionIds = ModifierOptionIds,
        ModifierNames = ModifierNames,
        ModifierTotal = ModifierTotal,
    };

    public OrderItemInput ToInput() => new()
    {
        MenuItemId = MenuItemId,
        Quantity = Quantity,
        Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
        ModifierOptionIds = ModifierOptionIds,
    };
}
