using HotelPOS.Contracts.Billing;
using HotelPOS.Domain.Common;

namespace HotelPOS.Domain.Billing;

public sealed class PaymentMethod : BaseEntity
{
    private PaymentMethod()
    {
    }

    public PaymentMethod(string name, string code, bool requiresReference, int sortOrder)
    {
        Update(name, code, requiresReference, sortOrder);
        IsActive = true;
    }

    public string Name { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;

    public bool RequiresReference { get; private set; }

    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }

    // Only cash can be over-tendered; the difference is handed back as change.
    public bool IsCash => Code == PaymentMethodCodes.Cash;

    public void Update(string name, string code, bool requiresReference, int sortOrder)
    {
        var trimmedName = name?.Trim() ?? string.Empty;
        if (trimmedName.Length is 0 or > BillingLimits.NameMaxLength)
        {
            throw new DomainException($"Payment method name must be 1 to {BillingLimits.NameMaxLength} characters.");
        }

        var normalizedCode = code?.Trim().ToUpperInvariant() ?? string.Empty;
        if (normalizedCode.Length is 0 or > BillingLimits.CodeMaxLength)
        {
            throw new DomainException($"Payment method code must be 1 to {BillingLimits.CodeMaxLength} characters.");
        }

        if (Id != 0 && IsCash && normalizedCode != PaymentMethodCodes.Cash)
        {
            throw new DomainException("The CASH code is used to compute change and cannot be renamed.");
        }

        Name = trimmedName;
        Code = normalizedCode;
        RequiresReference = requiresReference;
        SortOrder = sortOrder;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
