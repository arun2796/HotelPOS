using System.Text.Json;
using HotelPOS.Client.Services;
using HotelPOS.Contracts.Realtime;

namespace HotelPOS.Android.Services;

/// <summary>The server address and app identity on Android. The address is kept in Preferences and can change on the Server page.</summary>
public sealed class MauiClientHost : IClientHost
{
    private const string ServerKey = "hotelpos.server";
    private Uri? _server;

    public MauiClientHost()
    {
        var saved = Preferences.Default.Get(ServerKey, string.Empty);
        _server = Uri.TryCreate(saved, UriKind.Absolute, out var uri) ? uri : null;
    }

    public Uri ServerBase => _server ?? throw new InvalidOperationException("The server address has not been set yet.");

    public bool IsConfigured => _server is not null;

    public string DeviceNamePrefix => "APP";

    public string? AppVersion => AppInfo.Current.VersionString;

    public bool CanChangeServer => true;

    public void SetServer(Uri server)
    {
        _server = server;
        Preferences.Default.Set(ServerKey, server.ToString());
    }
}

/// <summary>Validation of a typed server address, the same rules as the desktop's first-run screen.</summary>
public static class ServerAddress
{
    public static bool TryNormalize(string? input, out Uri uri, out string? error)
    {
        uri = null!;
        error = null;
        var text = (input ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            error = "Enter the server address.";
            return false;
        }

        if (!text.Contains("://", StringComparison.Ordinal))
        {
            text = "http://" + text;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(parsed.Host))
        {
            error = "Use the form http://192.168.1.100:5000 or https://hotelpos-server:5001.";
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.Query) || !string.IsNullOrEmpty(parsed.Fragment))
        {
            error = "The address must not contain ? or #.";
            return false;
        }

        uri = new Uri(parsed.GetLeftPart(UriPartial.Authority) + "/");
        return true;
    }
}

/// <summary>Device-local key/value storage (device name, drafts, preferences) in Preferences, with an index so prefixes can be listed.</summary>
public sealed class MauiStorage : IBrowserStorage
{
    private const string IndexKey = "hotelpos.storage.keys";
    private readonly object _gate = new();

    public ValueTask<string?> GetAsync(string key) =>
        new(Preferences.Default.ContainsKey(key) ? Preferences.Default.Get(key, string.Empty) : null);

    public ValueTask SetAsync(string key, string value)
    {
        lock (_gate)
        {
            Preferences.Default.Set(key, value);
            var keys = ReadIndex();
            if (keys.Add(key))
            {
                WriteIndex(keys);
            }
        }

        return default;
    }

    public ValueTask RemoveAsync(string key)
    {
        lock (_gate)
        {
            Preferences.Default.Remove(key);
            var keys = ReadIndex();
            if (keys.Remove(key))
            {
                WriteIndex(keys);
            }
        }

        return default;
    }

    public ValueTask<IReadOnlyList<string>> KeysAsync(string prefix)
    {
        lock (_gate)
        {
            return new(ReadIndex().Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList());
        }
    }

    private static HashSet<string> ReadIndex()
    {
        var json = Preferences.Default.Get(IndexKey, "[]");
        try
        {
            return JsonSerializer.Deserialize<HashSet<string>>(json) ?? new HashSet<string>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    private static void WriteIndex(HashSet<string> keys) => Preferences.Default.Set(IndexKey, JsonSerializer.Serialize(keys));
}

/// <summary>Attention signal on the phone: a short tone on the notification stream and a vibration (Phase 20.2).</summary>
/// <summary>
/// Attention on the phone or tablet: a tone pattern per kind of event at full notification volume, with a matching
/// vibration. A kitchen hears a new order (three rising beeps) differently from a delayed ticket (a long alarm), and a
/// waiter hears a ready order (two beeps) differently from a bill request (one beep). The device's notification
/// volume still applies: turn it up on a kitchen tablet.
/// </summary>
public sealed class MauiAlertSignal : IAlertSignal
{
    private global::Android.Media.ToneGenerator? _tone;

    public ValueTask SignalAsync() => SignalAsync(NotificationKind.ServerNotice);

    public async ValueTask SignalAsync(NotificationKind kind)
    {
        var pattern = PatternFor(kind);
        try
        {
            Vibration.Default.Vibrate(TimeSpan.FromMilliseconds(pattern.VibrateMs));
        }
        catch (Exception)
        {
            // No vibrator, or the permission was withdrawn: carry on to the tone.
        }

        try
        {
            _tone ??= new global::Android.Media.ToneGenerator(global::Android.Media.Stream.Notification, 100);
            foreach (var (tone, ms) in pattern.Tones)
            {
                _tone.StartTone(tone, ms);
                await Task.Delay(ms + 80);
            }
        }
        catch (Exception)
        {
            // Silent device or no audio output.
        }
    }

    private static (IReadOnlyList<(global::Android.Media.Tone Tone, int Ms)> Tones, int VibrateMs) PatternFor(NotificationKind kind) => kind switch
    {
        // Kitchen: a new ticket must cut through the noise; a delay nags.
        NotificationKind.TicketCreated => (new[] { (global::Android.Media.Tone.PropBeep, 180), (global::Android.Media.Tone.PropBeep2, 180), (global::Android.Media.Tone.PropAck, 320) }, 600),
        NotificationKind.TicketDelayed => (new[] { (global::Android.Media.Tone.CdmaAlertCallGuard, 900), (global::Android.Media.Tone.CdmaAlertCallGuard, 900) }, 1200),
        // Waiter: food ready = two bright beeps; a bill or payment = one acknowledgement.
        NotificationKind.OrderReady => (new[] { (global::Android.Media.Tone.PropBeep2, 220), (global::Android.Media.Tone.PropBeep2, 220) }, 450),
        NotificationKind.OrderAccepted or NotificationKind.OrderPreparing => (new[] { (global::Android.Media.Tone.PropBeep, 160) }, 150),
        NotificationKind.BillRequested or NotificationKind.BillClaimed or NotificationKind.PaymentCompleted => (new[] { (global::Android.Media.Tone.PropAck, 300) }, 300),
        // Something went wrong: a low double buzz.
        NotificationKind.PrintFailed or NotificationKind.PaymentFailed or NotificationKind.DeviceOffline => (new[] { (global::Android.Media.Tone.PropNack, 350), (global::Android.Media.Tone.PropNack, 350) }, 700),
        _ => (new[] { (global::Android.Media.Tone.PropBeep, 250) }, 300),
    };
}

/// <summary>The refresh token, and only the refresh token, in Android SecureStorage (Keystore-backed).</summary>
public sealed class MauiSecureTokenStore : ISecureTokenStore
{
    private const string Key = "hotelpos.refresh-token";

    public async ValueTask<string?> GetRefreshTokenAsync()
    {
        try
        {
            return await SecureStorage.Default.GetAsync(Key);
        }
        catch (Exception)
        {
            // A corrupted keystore entry (after a restore from backup, for example) behaves like "signed out".
            SecureStorage.Default.Remove(Key);
            return null;
        }
    }

    public async ValueTask SetRefreshTokenAsync(string? refreshToken)
    {
        if (string.IsNullOrEmpty(refreshToken))
        {
            SecureStorage.Default.Remove(Key);
            return;
        }

        await SecureStorage.Default.SetAsync(Key, refreshToken);
    }
}
