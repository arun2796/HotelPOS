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
        PackagingCharge = line.PackagingCharge;
        _quantity = line.Quantity;
        _notes = line.Notes;
        _isParcel = line.IsParcel;
        _seatCode = line.SeatCode;
        PortionId = line.PortionId;
        PortionName = line.PortionName;
        PortionQuantity = line.PortionQuantity;
        PortionUnit = line.PortionUnit;
    }

    public int MenuItemId { get; }
    public string Name { get; }
    public decimal UnitPrice { get; }
    public IReadOnlyList<int> ModifierOptionIds { get; }
    public IReadOnlyList<string> ModifierNames { get; }
    public decimal ModifierTotal { get; }

    // The portion ordered; PortionName is set only for an item sold in several portions (an item with one price shows none).
    public int? PortionId { get; private set; }
    public string? PortionName { get; }
    public decimal? PortionQuantity { get; }
    public string? PortionUnit { get; }

    // A loaded draft line whose portion is no longer on the menu: it has to be removed and added again before sending.
    [ObservableProperty]
    private bool _needsPortionChoice;

    public void ResolvePortion(int portionId)
    {
        PortionId = portionId;
        NeedsPortionChoice = false;
    }

    // What the menu charges to pack one of this item; added to the price while the line is a parcel.
    public decimal PackagingCharge { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LineTotal), nameof(LineTotalText))]
    private int _quantity;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LineTotal), nameof(LineTotalText), nameof(DisplayName), nameof(DetailText), nameof(HasDetail), nameof(ParcelText))]
    private bool _isParcel;

    // "4A", or null for the whole table.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailText), nameof(HasDetail))]
    private string? _seatCode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailText), nameof(HasDetail))]
    private string? _notes;

    public decimal LineTotal => (UnitPrice + (IsParcel ? PackagingCharge : 0m) + ModifierTotal) * Quantity;

    public string DisplayName => HotelPOS.Contracts.Menu.ItemDisplayName.Compose(IsParcel ? $"{Name} (Parcel)" : Name, PortionName);

    public string ParcelText => IsParcel ? "Parcel ✓" : "Parcel";

    public string LineTotalText => LineTotal.ToString("N2", CultureInfo.CurrentCulture);

    public string DetailText => string.Join(" · ",
        (string.IsNullOrWhiteSpace(SeatCode) ? Array.Empty<string>() : new[] { $"Seat {SeatCode}" })
        .Concat(IsParcel ? new[] { PackagingCharge > 0 ? $"Parcel +{PackagingCharge.ToString("N2", CultureInfo.CurrentCulture)}" : "Parcel" } : Array.Empty<string>())
        .Concat(ModifierNames)
        .Concat(string.IsNullOrWhiteSpace(Notes) ? Array.Empty<string>() : new[] { $"“{Notes}”" }));

    public bool HasDetail => DetailText.Length > 0;

    public bool CanIncrease => Quantity < OrderLimits.MaxQuantity;

    public bool IsSameChoice(int menuItemId, IReadOnlyCollection<int> optionIds, bool isParcel = false, string? seatCode = null, int? portionId = null) =>
        MenuItemId == menuItemId && PortionId == portionId && string.IsNullOrWhiteSpace(Notes) && IsParcel == isParcel
        && string.Equals(SeatCode ?? string.Empty, seatCode ?? string.Empty, StringComparison.OrdinalIgnoreCase)
        && ModifierOptionIds.Count == optionIds.Count
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
        IsParcel = IsParcel,
        PackagingCharge = PackagingCharge,
        SeatCode = SeatCode,
        PortionId = PortionId,
        PortionName = PortionName,
        PortionQuantity = PortionQuantity,
        PortionUnit = PortionUnit,
    };

    public OrderItemInput ToInput() => new()
    {
        MenuItemId = MenuItemId,
        Quantity = Quantity,
        Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
        ModifierOptionIds = ModifierOptionIds,
        IsParcel = IsParcel,
        SeatCode = string.IsNullOrWhiteSpace(SeatCode) ? null : SeatCode,
        PortionId = PortionId,
    };
}
