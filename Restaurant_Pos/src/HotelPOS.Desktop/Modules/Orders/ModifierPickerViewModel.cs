using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using HotelPOS.Contracts.Menu;
using HotelPOS.Desktop.Services.Orders;

namespace HotelPOS.Desktop.Modules.Orders;

public sealed partial class PickerOptionViewModel : ObservableObject
{
    public PickerOptionViewModel(MenuModifierOptionDto option)
    {
        Option = option;
        PriceText = option.PriceDelta == 0 ? string.Empty
            : option.PriceDelta.ToString("+0.##;-0.##", CultureInfo.CurrentCulture);
    }

    public MenuModifierOptionDto Option { get; }

    public string Name => Option.Name;

    public string PriceText { get; }

    [ObservableProperty]
    private bool _isSelected;
}

public sealed partial class PickerGroupViewModel : ObservableObject
{
    public PickerGroupViewModel(MenuModifierGroupDto group)
    {
        Group = group;
        Options = group.Options.Select(o => new PickerOptionViewModel(o)).ToList();
    }

    public MenuModifierGroupDto Group { get; }

    public IReadOnlyList<PickerOptionViewModel> Options { get; }

    public string Name => Group.Name;

    public string RuleText => Group.MinSelections == Group.MaxSelections
        ? $"Choose {Group.MinSelections}"
        : Group.MinSelections == 0 ? $"Optional · up to {Group.MaxSelections}" : $"Choose {Group.MinSelections} to {Group.MaxSelections}";

    public int SelectedCount => Options.Count(o => o.IsSelected);

    public bool IsSatisfied => SelectedCount >= Group.MinSelections && SelectedCount <= Group.MaxSelections;

    public void Toggle(PickerOptionViewModel option)
    {
        if (option.IsSelected)
        {
            option.IsSelected = false;
        }
        else if (Group.MaxSelections == 1)
        {
            foreach (var other in Options)
            {
                other.IsSelected = ReferenceEquals(other, option);
            }
        }
        else if (SelectedCount < Group.MaxSelections)
        {
            option.IsSelected = true;
        }

        OnPropertyChanged(nameof(IsSatisfied));
    }
}

// One portion in the picker: "Half 180", with the quantity label and whether it is the one selected.
public sealed partial class PickerPortionViewModel : ObservableObject
{
    public PickerPortionViewModel(MenuItemPortionDto portion)
    {
        Portion = portion;
        PriceText = portion.Price.ToString("N2", CultureInfo.CurrentCulture);
        QuantityText = portion.Quantity is { } quantity
            ? quantity.ToString("0.###", CultureInfo.CurrentCulture) + (string.IsNullOrEmpty(portion.Unit) ? string.Empty : " " + portion.Unit)
            : string.Empty;
    }

    public MenuItemPortionDto Portion { get; }

    public string Name => Portion.Name;

    public string PriceText { get; }

    public string QuantityText { get; }

    public bool HasQuantity => QuantityText.Length > 0;

    [ObservableProperty]
    private bool _isSelected;
}

public sealed partial class ModifierPickerViewModel : ObservableObject
{
    public ModifierPickerViewModel(MenuEntryDto item, IEnumerable<MenuModifierGroupDto> groups)
    {
        Item = item;
        Groups = groups.Select(g => new PickerGroupViewModel(g)).ToList();
        Portions = item.Portions.OrderBy(p => p.SortOrder).ThenBy(p => p.Id).Select(p => new PickerPortionViewModel(p)).ToList();

        // The default portion is selected first; an item with one price has nothing to choose.
        var first = item.Portions.FirstOrDefault(p => p.IsDefault) ?? item.Portions.FirstOrDefault();
        foreach (var portion in Portions)
        {
            portion.IsSelected = HasPortions && portion.Portion.Id == first?.Id;
        }
    }

    // The portions of an item sold in several (the picker shows the Portion group only then).
    public IReadOnlyList<PickerPortionViewModel> Portions { get; }

    public bool HasPortions => Portions.Count > 1;

    public MenuItemPortionDto? SelectedPortion => Portions.FirstOrDefault(p => p.IsSelected)?.Portion;

    // The price the picker shows: the chosen portion's, else the item's (one price, or a server without portions).
    public decimal CurrentPrice => SelectedPortion?.Price ?? Item.Price;

    public string CurrentPriceText => CurrentPrice.ToString("N2", CultureInfo.CurrentCulture);

    public void Select(PickerPortionViewModel? portion)
    {
        if (portion is null || !Portions.Contains(portion))
        {
            return;
        }

        foreach (var other in Portions)
        {
            other.IsSelected = ReferenceEquals(other, portion);
        }

        ErrorMessage = null;
        OnPropertyChanged(nameof(SelectedPortion));
        OnPropertyChanged(nameof(CurrentPrice));
        OnPropertyChanged(nameof(CurrentPriceText));
    }

    public MenuEntryDto Item { get; }

    public IReadOnlyList<PickerGroupViewModel> Groups { get; }

    [ObservableProperty]
    private int _quantity = 1;

    [ObservableProperty]
    private string? _errorMessage;

    public PickerGroupViewModel? GroupOf(PickerOptionViewModel option) => Groups.FirstOrDefault(g => g.Options.Contains(option));

    public DraftLine? TryBuild()
    {
        var missing = Groups.FirstOrDefault(g => !g.IsSatisfied);
        if (missing is not null)
        {
            ErrorMessage = $"{missing.Name}: {missing.RuleText.ToLower(CultureInfo.CurrentCulture)}.";
            return null;
        }

        var chosen = Groups.SelectMany(g => g.Options).Where(o => o.IsSelected).Select(o => o.Option).ToList();
        // The portion: the one selected; an item with one portion uses it (its name is not shown); none at all (an older server)
        // is priced from the item.
        var portion = HasPortions ? SelectedPortion : Item.Portions.FirstOrDefault();
        return new DraftLine
        {
            MenuItemId = Item.Id,
            Name = Item.Name,
            UnitPrice = portion?.Price ?? Item.Price,
            PortionId = portion?.Id,
            PortionName = HasPortions ? portion?.Name : null,
            PortionQuantity = HasPortions ? portion?.Quantity : null,
            PortionUnit = HasPortions ? portion?.Unit : null,
            Quantity = Quantity,
            ModifierOptionIds = chosen.Select(o => o.Id).ToList(),
            ModifierNames = chosen.Select(o => o.Name).ToList(),
            ModifierTotal = chosen.Sum(o => o.PriceDelta),
        };
    }
}
