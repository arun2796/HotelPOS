using HotelPOS.Domain.Common;

namespace HotelPOS.Domain.Billing;

public sealed class PaymentMethod : BaseEntity
{
    private PaymentMethod()
    {
    }

    public PaymentMethod(string name, string code, bool requiresReference, int sortOrder)
    {
        Name = name.Trim();
        Code = code.Trim().ToUpperInvariant();
        RequiresReference = requiresReference;
        SortOrder = sortOrder;
        IsActive = true;
    }

    public string Name { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;

    public bool RequiresReference { get; private set; }

    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }
}
