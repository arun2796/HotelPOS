using HotelPOS.Domain.Common;

namespace HotelPOS.Domain.Menu;

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

    public void Deactivate() => IsActive = false;
}
