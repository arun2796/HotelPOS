using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Auth;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Security;

// The password rule currently configured in Settings.
public interface IPasswordPolicySource
{
    Task<PasswordRules> GetAsync(CancellationToken cancellationToken);
}

internal sealed class PasswordPolicySource : IPasswordPolicySource
{
    private static readonly string[] Keys =
    {
        SettingKeys.PasswordMinLength,
        SettingKeys.PasswordRequireLetter,
        SettingKeys.PasswordRequireDigit,
        SettingKeys.PasswordRequireUpperAndLower,
        SettingKeys.PasswordRequireSymbol,
    };

    private readonly IAppDbContext _db;

    public PasswordPolicySource(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<PasswordRules> GetAsync(CancellationToken cancellationToken)
    {
        var values = await _db.Settings.AsNoTracking()
            .Where(s => Keys.Contains(s.Key))
            .ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);
        return PasswordRules.FromSettings(key => values.GetValueOrDefault(key));
    }
}
