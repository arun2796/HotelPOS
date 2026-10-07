using HotelPOS.Contracts.Common;

namespace HotelPOS.Domain.Common;

public sealed class DomainException : Exception
{
    public DomainException(string message, string code = ErrorCodes.BusinessRule)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
