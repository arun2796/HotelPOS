using System.Text.Json;
using HotelPOS.Contracts.Realtime;

namespace HotelPOS.Client.Services;

/// <summary>
/// The device's way of getting attention: a short beep and, where the hardware has it, a vibration. The browser host
/// plays it through wwwroot/js/notify.js (AudioContext, navigator.vibrate); the Android host uses its own tone and Vibration.
/// </summary>
public interface IAlertSignal
{
    /// <summary>Beeps and vibrates once. Never throws: a device without audio or a vibrator is silent.</summary>
    ValueTask SignalAsync();
}

/// <summary>Used when a host registers nothing (tests, a future host): no sound, no vibration.</summary>
public sealed class SilentAlertSignal : IAlertSignal
{
    public ValueTask SignalAsync() => default;
}

/// <summary>
/// What this browser or phone does for each kind of notification: toast on/off, sound (and vibration) on/off. Kept in
/// <see cref="IBrowserStorage"/>, so it is per device, never a server setting. Kinds never changed follow
/// <see cref="NotificationPreferenceDefaults"/> for the signed-in roles.
/// </summary>
public sealed class NotificationPreferences
{
    public const string StorageKey = "notify.prefs.v1";

    private readonly IBrowserStorage _storage;
    private readonly WebAuthState _auth;
    private Dictionary<string, NotificationPreference> _saved = new(StringComparer.Ordinal);
    private bool _loaded;

    public NotificationPreferences(IBrowserStorage storage, WebAuthState auth)
    {
        _storage = storage;
        _auth = auth;
    }

    public async Task LoadAsync()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        try
        {
            var json = await _storage.GetAsync(StorageKey);
            if (!string.IsNullOrEmpty(json))
            {
                _saved = JsonSerializer.Deserialize<Dictionary<string, NotificationPreference>>(json) ?? _saved;
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            // Unreadable preferences behave like never-configured ones.
            _saved = new Dictionary<string, NotificationPreference>(StringComparer.Ordinal);
        }
    }

    public NotificationPreference Get(NotificationKind kind) =>
        _saved.TryGetValue(kind.ToString(), out var preference)
            ? preference
            : NotificationPreferenceDefaults.For(kind, _auth.User?.Roles ?? Array.Empty<string>());

    public async Task SetAsync(NotificationKind kind, bool toast, bool sound)
    {
        _saved[kind.ToString()] = new NotificationPreference(toast, sound);
        await _storage.SetAsync(StorageKey, JsonSerializer.Serialize(_saved));
    }
}
