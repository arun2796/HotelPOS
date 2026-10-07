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
        Update(name, code, sortOrder);
        IsActive = true;
    }

    public string Name { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }

    public void Update(string name, string code, int sortOrder)
    {
        Name = MenuGuard.Name(name, "Station name");
        Code = MenuGuard.Code(code, "Station code") ?? throw new DomainException("Station code is required.");
        SortOrder = sortOrder;
    }

    public void Activate() => IsActive = true;

    /// <summary>The caller checks first that no active item is prepared here.</summary>
    public void Deactivate() => IsActive = false;
}
