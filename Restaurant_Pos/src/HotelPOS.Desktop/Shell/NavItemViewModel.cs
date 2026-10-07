using CommunityToolkit.Mvvm.ComponentModel;
using HotelPOS.Desktop.Services.Navigation;

namespace HotelPOS.Desktop.Shell;

public sealed partial class NavItemViewModel : ObservableObject
{
    public NavItemViewModel(ModuleDefinition module, bool showGroupHeader)
    {
        Module = module;
        ShowGroupHeader = showGroupHeader;
    }

    public ModuleDefinition Module { get; }

    public string Key => Module.Key;

    public string Title => Module.Title;

    public string Glyph => Module.Glyph;

    public string Group => Module.Group;

    /// <summary>True for the first item of each group, which draws the group caption above it.</summary>
    public bool ShowGroupHeader { get; }

    /// <summary>Modules planned for a later phase are listed but show a placeholder page.</summary>
    public bool IsAvailable => Module.ViewModelType is not null;
}
