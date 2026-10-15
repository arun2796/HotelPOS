using HotelPOS.Application.Common.Interfaces;
using Microsoft.AspNetCore.Identity;

namespace HotelPOS.Infrastructure.Identity;

public sealed class PasswordHasherAdapter : IPasswordHasher
{
    private static readonly object HashUser = new();
    private readonly PasswordHasher<object> _inner = new();

    public string Hash(string password) => _inner.HashPassword(HashUser, password);

    public PasswordCheck Verify(string passwordHash, string providedPassword)
    {
        try
        {
            return _inner.VerifyHashedPassword(HashUser, passwordHash, providedPassword) switch
            {
                PasswordVerificationResult.Success => PasswordCheck.Success,
                PasswordVerificationResult.SuccessRehashNeeded => PasswordCheck.SuccessRehashNeeded,
                _ => PasswordCheck.Failed,
            };
        }
        catch (FormatException)
        {
            // A corrupted hash must never authenticate anyone.
            return PasswordCheck.Failed;
        }
    }
}
