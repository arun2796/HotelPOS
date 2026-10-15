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

public sealed partial class ModifierPickerViewModel : ObservableObject
{
    public ModifierPickerViewModel(MenuEntryDto item, IEnumerable<MenuModifierGroupDto> groups)
    {
        Item = item;
        Groups = groups.Select(g => new PickerGroupViewModel(g)).ToList();
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
        return new DraftLine
        {
            MenuItemId = Item.Id,
            Name = Item.Name,
            UnitPrice = Item.Price,
            Quantity = Quantity,
            ModifierOptionIds = chosen.Select(o => o.Id).ToList(),
            ModifierNames = chosen.Select(o => o.Name).ToList(),
            ModifierTotal = chosen.Sum(o => o.PriceDelta),
        };
    }
}
