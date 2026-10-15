using HotelPOS.Contracts.Customers;
using HotelPOS.Domain.Customers;

namespace HotelPOS.Application.Features.Customers;

internal static class CustomerMapping
{
    public static CustomerDto ToDto(Customer customer) => new()
    {
        Id = customer.Id,
        Name = customer.Name,
        Phone = customer.Phone,
        Email = customer.Email,
        Gstin = customer.Gstin,
        LegalName = customer.LegalName,
        StateCode = customer.StateCode,
        Address = customer.Address,
        IsActive = customer.IsActive,
    };
}
