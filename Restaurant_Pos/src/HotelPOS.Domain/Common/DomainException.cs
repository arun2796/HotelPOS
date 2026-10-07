using HotelPOS.Contracts.Common;

namespace HotelPOS.Domain.Common;

/// <summary>
/// Raised when an entity method is called in a way that breaks a domain invariant. Application
/// services normally check rules first and return a failed result; this is the safety net.
/// </summary>
public sealed class DomainException : Exception
{
    public DomainException(string message, string code = ErrorCodes.BusinessRule)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
