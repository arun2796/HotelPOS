using CommunityToolkit.Mvvm.ComponentModel;
using HotelPOS.Contracts.Menu;

namespace HotelPOS.Desktop.Modules.MenuAdmin;

// One row of the Portions & prices table of the item form (Quarter / Half / Full...). The text fields stay text until the
// form is validated, so a half-typed price never throws.
public sealed partial class PortionRowViewModel : ObservableObject
{
    // The unit list of the combo box: an empty choice (no quantity label) first.
    public static IReadOnlyList<string> UnitChoices { get; } = new[] { string.Empty }.Concat(PortionUnits.All).ToList();

    // The portion being kept (null for a row added in this form).
    public int? Id { get; set; }

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _quantityText = string.Empty;

    [ObservableProperty]
    private string _unit = string.Empty;

    [ObservableProperty]
    private string _priceText = string.Empty;

    [ObservableProperty]
    private bool _isDefault;

    [ObservableProperty]
    private bool _isActive = true;

    // What is wrong with this row after a failed save, shown under it.
    [ObservableProperty]
    private string? _error;

    // The row of the portion whose chip was clicked in the item list: the form highlights it.
    [ObservableProperty]
    private bool _isFocused;
}

// One chip of the item list: "Half 180", with the default marked and a switched-off portion shown struck through.
public sealed record PortionChip(string Text, bool IsDefault, bool IsOff, int ItemId = 0, int PortionId = 0);

// A row of the item list: the item, its portion chips, and its picture (loaded in the background).
public sealed partial class MenuItemRowViewModel : ObservableObject
{
    public MenuItemRowViewModel(MenuItemDto item)
    {
        Item = item;
        PortionChips = item.Portions.Count == 0
            // A server that does not send portions: the item is its one price.
            ? new[] { new PortionChip($"Regular {FormNumbers.Money(item.Price)}", true, false, item.Id, 0) }
            : item.Portions
                .OrderBy(p => p.SortOrder).ThenBy(p => p.Id)
                .Select(p => new PortionChip($"{p.Name} {FormNumbers.Money(p.Price)}", p.IsDefault, !p.IsActive, item.Id, p.Id))
                .ToList();
        Initials = Dashboard.Initials.Of(item.Name);
    }

    public MenuItemDto Item { get; }

    public IReadOnlyList<PortionChip> PortionChips { get; }

    // Shown on the tile while the item has no picture.
    public string Initials { get; }

    public bool HasPortions => PortionChips.Count > 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPicture))]
    private string? _imageFile;

    public bool HasPicture => ImageFile is not null;
}
