using HotelPOS.Domain.Common;

namespace HotelPOS.Domain.Menu;

/// <summary>A tax rate applied to items (e.g. GST 5%). Orders snapshot the rate when items are added.</summary>
public sealed class Tax : BaseEntity
{
    private Tax()
    {
    }

    public Tax(string name, string code, decimal ratePercent)
    {
        Update(name, code, ratePercent);
        IsActive = true;
    }

    public string Name { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;
    public decimal RatePercent { get; private set; }
    public bool IsActive { get; private set; }

    public void Update(string name, string code, decimal ratePercent)
    {
        Name = MenuGuard.Name(name, "Tax name");
        Code = MenuGuard.Code(code, "Tax code") ?? throw new DomainException("Tax code is required.");
        RatePercent = MenuGuard.Money(ratePercent, 0m, 100m, "Tax rate");
    }

    public void Activate() => IsActive = true;

    /// <summary>The caller checks first that no active item uses the tax.</summary>
    public void Deactivate() => IsActive = false;
}
