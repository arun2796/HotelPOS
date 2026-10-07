using HotelPOS.Domain.Common;

namespace HotelPOS.Domain.Menu;

/// <summary>Where an item is prepared: Main Kitchen, Bar, Bakery... Kitchen tickets are routed by station.</summary>
public sealed class PreparationStation : BaseEntity
{
    private PreparationStation()
    {
    }

    public PreparationStation(string name, string code, int sortOrder)
    {
        Name = name.Trim();
        Code = code.Trim().ToUpperInvariant();
        SortOrder = sortOrder;
        IsActive = true;
    }

    public string Name { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }
}
