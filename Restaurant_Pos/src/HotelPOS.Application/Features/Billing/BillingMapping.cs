using HotelPOS.Contracts.Billing;
using HotelPOS.Domain.Billing;

namespace HotelPOS.Application.Features.Billing;

internal static class BillingMapping
{
    public static DiscountDto ToDto(Discount d) => new()
    {
        Id = d.Id,
        Name = d.Name,
        Type = d.Type,
        Value = d.Value,
        RequiresApproval = d.RequiresApproval,
        IsActive = d.IsActive,
    };

    public static PaymentMethodDto ToDto(PaymentMethod m) => new()
    {
        Id = m.Id,
        Name = m.Name,
        Code = m.Code,
        RequiresReference = m.RequiresReference,
        IsCash = m.IsCash,
        SortOrder = m.SortOrder,
        IsActive = m.IsActive,
    };
}
