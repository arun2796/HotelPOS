using HotelPOS.Contracts.Auth;

namespace HotelPOS.Desktop.Services.Api;

public static class PasswordRulesExtensions
{
    // The password rule the admin configured (public settings, open to every signed-in user); null when the server
    // cannot be asked, in which case the client checks nothing and shows the server's message instead.
    public static async Task<PasswordRules?> GetPasswordRulesAsync(this ISystemApi api, CancellationToken cancellationToken = default)
    {
        var settings = (await api.GetPublicSettingsAsync(cancellationToken))?.Data;
        return settings is null
            ? null
            : PasswordRules.FromSettings(key => settings.FirstOrDefault(s => s.Key == key)?.Value);
    }
}
