using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Enums;
using HotelPOS.Domain.Common;

namespace HotelPOS.Domain.Billing;

public sealed class Discount : BaseEntity
{
    private Discount()
    {
    }

    public Discount(string name, DiscountType type, decimal value, bool requiresApproval)
    {
        Update(name, type, value, requiresApproval);
        IsActive = true;
    }

    public string Name { get; private set; } = string.Empty;
    public DiscountType Type { get; private set; }
    public decimal Value { get; private set; }
    public bool RequiresApproval { get; private set; }
    public bool IsActive { get; private set; }

    public void Update(string name, DiscountType type, decimal value, bool requiresApproval)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > BillingLimits.NameMaxLength)
        {
            throw new DomainException($"Discount name must be 1 to {BillingLimits.NameMaxLength} characters.");
        }

        CheckValue(type, value);
        Name = trimmed;
        Type = type;
        Value = value;
        RequiresApproval = requiresApproval;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    public static void CheckValue(DiscountType type, decimal value)
    {
        if (!Enum.IsDefined(type))
        {
            throw new DomainException("Choose a percentage or a fixed amount.");
        }

        if (value <= 0 || decimal.Round(value, 2) != value)
        {
            throw new DomainException("The discount must be more than zero, with at most two decimals.");
        }

        if (type == DiscountType.Percentage && value > 100)
        {
            throw new DomainException("A percentage discount cannot exceed 100%.");
        }

        if (value > BillingLimits.MaxAmount)
        {
            throw new DomainException("The discount amount is too large.");
        }
    }
}
