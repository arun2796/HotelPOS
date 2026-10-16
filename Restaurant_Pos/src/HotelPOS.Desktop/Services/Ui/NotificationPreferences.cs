using HotelPOS.Contracts.Realtime;
using HotelPOS.Desktop.Services.Auth;
using HotelPOS.Desktop.Services.Configuration;

namespace HotelPOS.Desktop.Services.Ui;

// What this terminal does for each kind of notification: toast on/off, sound on/off. Stored per terminal and profile
// in preferences.json (not a server setting). Until a kind is changed, the role-based defaults apply
// (NotificationPreferenceDefaults): sound only for kitchen tickets, cashier bill requests and failed prints.
public sealed class NotificationPreferences
{
    private readonly IUserPreferences _preferences;
    private readonly IAuthSession _session;

    public NotificationPreferences(IUserPreferences preferences, IAuthSession session)
    {
        _preferences = preferences;
        _session = session;
    }

    public NotificationPreference Get(NotificationKind kind) =>
        _preferences.GetNotification(kind)
        ?? NotificationPreferenceDefaults.For(kind, _session.User?.Roles ?? Array.Empty<string>());

    public void Set(NotificationKind kind, bool toast, bool sound) =>
        _preferences.SetNotification(kind, new NotificationPreference(toast, sound));
}
