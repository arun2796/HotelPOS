namespace HotelPOS.Client.Shared;

/// <summary>One entry of <c>Tabs</c> or <c>Segmented</c>: a stable key, the words, and an optional count badge.</summary>
public sealed record TabItem(string Key, string Label, int? Count = null);
