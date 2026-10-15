using HotelPOS.Contracts.Gst;
using HotelPOS.Domain.Common;

namespace HotelPOS.Domain.Customers;

// The customer master (Phase 16, filled by import). Bills still carry their own customer snapshot.
public sealed class Customer : BaseEntity
{
    private Customer()
    {
    }

    public Customer(string name, string? phone, string? email, string? gstin, string? legalName, string? stateCode, string? address)
    {
        Update(name, phone, email, gstin, legalName, stateCode, address);
        IsActive = true;
    }

    public string Name { get; private set; } = string.Empty;
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string? Gstin { get; private set; }
    public string? LegalName { get; private set; }
    public string? StateCode { get; private set; }
    public string? Address { get; private set; }
    public bool IsActive { get; private set; }

    public static string? NormalizePhone(string? phone)
    {
        var digits = new string((phone ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        // Indian numbers are kept as the 10 national digits (+91 and a leading 0 are dropped) so that the same customer matches.
        digits = digits.Length == 12 && digits.StartsWith("91", StringComparison.Ordinal) ? digits[2..] : digits.Length == 11 && digits[0] == (char)48 ? digits[1..] : digits;
        return digits.Length == 0 ? null : digits;
    }

    public void Update(string name, string? phone, string? email, string? gstin, string? legalName, string? stateCode, string? address)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
        {
            throw new DomainException("A customer needs a name of at most 200 characters.");
        }

        Name = name.Trim();
        Phone = NormalizePhone(phone);
        Email = Clean(email);
        Gstin = Clean(gstin)?.ToUpperInvariant();
        LegalName = Clean(legalName);
        StateCode = Clean(stateCode);
        Address = Clean(address);
    }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
