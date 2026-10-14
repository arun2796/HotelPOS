using System.Collections.Concurrent;

namespace HotelPOS.Application.Security;

public interface IDeviceAccessCache
{
    bool? IsDeactivated(Guid deviceId);

    void Remember(Guid deviceId, bool deactivated);

    void Forget(Guid deviceId);
}

// Every authenticated request checks the device; the answer is kept in memory and dropped the moment an admin
// activates or deactivates the device, so the check costs a query only once per device.
public sealed class DeviceAccessCache : IDeviceAccessCache
{
    private readonly ConcurrentDictionary<Guid, bool> _deactivated = new();

    public bool? IsDeactivated(Guid deviceId) => _deactivated.TryGetValue(deviceId, out var value) ? value : null;

    public void Remember(Guid deviceId, bool deactivated) => _deactivated[deviceId] = deactivated;

    public void Forget(Guid deviceId) => _deactivated.TryRemove(deviceId, out _);
}
