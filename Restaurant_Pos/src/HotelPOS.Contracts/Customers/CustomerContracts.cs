using HotelPOS.Contracts.Common;

namespace HotelPOS.Contracts.Customers;

public static class CustomerLimits
{
    public const int NameMaxLength = 100;
    public const int PhoneMaxLength = 20;
    public const int EmailMaxLength = 200;
    public const int LegalNameMaxLength = 200;
    public const int AddressMaxLength = 500;
    public const int SearchMaxLength = 50;

    // The picker on the bill shows a short list; the customer screen pages.
    public const int PickerPageSize = 8;
}

public sealed record CustomerDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string? Gstin { get; init; }
    public string? LegalName { get; init; }
    public string? StateCode { get; init; }
    public string? Address { get; init; }
    public bool IsActive { get; init; }
}

// Search matches the name, the phone digits or the GSTIN; inactive customers are left out unless asked for.
public sealed record CustomerQuery : PagedQuery
{
    public string? Search { get; init; }
    public bool IncludeInactive { get; init; }
}

public sealed record SaveCustomerRequest
{
    public string Name { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string? Gstin { get; init; }
    public string? LegalName { get; init; }
    public string? StateCode { get; init; }
    public string? Address { get; init; }
}
