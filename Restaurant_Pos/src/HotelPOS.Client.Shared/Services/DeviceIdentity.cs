using System.Security.Cryptography;
using HotelPOS.Contracts.Enums;

namespace HotelPOS.Client.Services;

/// <summary>
/// A browser has no terminal name, so one is generated once per browser profile and kept in localStorage, e.g.
/// WEB-WAITER-3F9A1C. The server registers it through the normal login (DeviceRegistry upserts by name), so it shows in
/// Administration › Devices and can be renamed or deactivated there. Clearing site data creates a new device.
/// </summary>
public sealed class DeviceIdentity
{
    public const string ModeKey = "hotelpos.mode";
    private const string DeviceKeyPrefix = "hotelpos.device.";

    private readonly IBrowserStorage _storage;
    private readonly IClientHost _host;

    public DeviceIdentity(IBrowserStorage storage, IClientHost host)
    {
        _storage = storage;
        _host = host;
    }

    /// <summary>The device types a browser can act as. Billing and Admin stay on the desktop client.</summary>
    public static IReadOnlyList<DeviceType> BrowserDeviceTypes { get; } = new[] { DeviceType.Waiter, DeviceType.Kitchen };

    public async ValueTask<DeviceType> GetPreferredTypeAsync()
    {
        var stored = await _storage.GetAsync(ModeKey);
        return Enum.TryParse<DeviceType>(stored, out var type) && BrowserDeviceTypes.Contains(type) ? type : DeviceType.Waiter;
    }

    public ValueTask SetPreferredTypeAsync(DeviceType type) => _storage.SetAsync(ModeKey, type.ToString());

    public async ValueTask<string> GetOrCreateNameAsync(DeviceType type)
    {
        var key = DeviceKeyPrefix + type.ToString().ToUpperInvariant();
        var existing = await _storage.GetAsync(key);
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return existing;
        }

        var name = $"{_host.DeviceNamePrefix}-{type.ToString().ToUpperInvariant()}-{RandomSuffix()}";
        await _storage.SetAsync(key, name);
        return name;
    }

    private static string RandomSuffix()
    {
        Span<byte> bytes = stackalloc byte[3];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes);
    }
}
