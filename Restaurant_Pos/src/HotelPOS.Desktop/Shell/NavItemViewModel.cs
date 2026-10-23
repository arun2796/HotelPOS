using CommunityToolkit.Mvvm.ComponentModel;
using HotelPOS.Desktop.Services.Navigation;

namespace HotelPOS.Desktop.Shell;

public sealed partial class NavItemViewModel : ObservableObject
{
    public NavItemViewModel(ModuleDefinition module, bool showGroupHeader, bool showGroupDivider = false)
    {
        Module = module;
        ShowGroupHeader = showGroupHeader;
        ShowGroupDivider = showGroupDivider;
    }

    public ModuleDefinition Module { get; }

    public string Key => Module.Key;

    public string Title => Module.Title;

    public string Glyph => Module.Glyph;

    public string Group => Module.Group;

    /// <summary>The group header as the sidebar prints it: small capitals, like the mock-up's section labels.</summary>
    public string GroupLabel => Module.Group.ToUpperInvariant();

    /// <summary>The first visible item of a group carries the group's header.</summary>
    public bool ShowGroupHeader { get; }

    /// <summary>A divider above the header of every group but the first.</summary>
    public bool ShowGroupDivider { get; }

    /// <summary>False for modules that stay reachable (palette, links) but have no sidebar entry, e.g. Customers.</summary>
    public bool ShowInNav => Module.ShowInNav;

    public bool IsAvailable => Module.ViewModelType is not null;

    /// <summary>A count shown on the item (failed prints on Print Log); 0 hides it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBadge), nameof(BadgeText))]
    private int _badge;

    public bool HasBadge => Badge > 0;

    public string BadgeText => Badge > 99 ? "99+" : Badge.ToString(System.Globalization.CultureInfo.CurrentCulture);
}
