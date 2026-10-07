using CommunityToolkit.Mvvm.ComponentModel;
using HotelPOS.Desktop.Services.Navigation;

namespace HotelPOS.Desktop.Modules.Common;

/// <summary>Shown for modules that are planned but not built yet.</summary>
public sealed class PlaceholderViewModel : ObservableObject
{
    public PlaceholderViewModel(ModuleDefinition module)
    {
        Title = module.Title;
        Glyph = module.Glyph;
        Description = module.Description;
        PhaseText = $"Coming in Phase {module.Phase}";
    }

    public string Title { get; }

    public string Glyph { get; }

    public string Description { get; }

    public string PhaseText { get; }
}
