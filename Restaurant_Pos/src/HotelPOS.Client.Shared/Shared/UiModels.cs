namespace HotelPOS.Client.Shared;

/// <summary>One entry of <c>Tabs</c> or <c>Segmented</c>: a stable key, the words, and an optional count badge.</summary>
public sealed record TabItem(string Key, string Label, int? Count = null);

/// <summary>What the <c>ItemOptionsSheet</c> hands back: the chosen options, the quantity and the trimmed kitchen note.</summary>
public sealed record ItemOptionsResult(IReadOnlyList<HotelPOS.Contracts.Menu.MenuModifierOptionDto> Options, int Quantity, string? Notes);

/// <summary>One row of an <c>ActionList</c>: a stable key, the words, an icon and how it is drawn.</summary>
public sealed record ActionItem(string Key, string Label, string Icon, ActionStyle Style = ActionStyle.Default, bool Disabled = false);

public enum ActionStyle
{
    Default,
    Primary,
    Danger,
}
