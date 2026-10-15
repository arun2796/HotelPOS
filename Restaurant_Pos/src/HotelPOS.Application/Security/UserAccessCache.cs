using System.Collections.Concurrent;

namespace HotelPOS.Application.Security;

public interface IUserAccessCache
{
    bool? IsDeactivated(int userId);

    void Remember(int userId, bool deactivated);
}

// The user counterpart of DeviceAccessCache: an access token stays valid for up to an hour, so without a
// per-request check a deactivated user could keep working until it expires. Set from the admin action after the
// commit, filled from the database on a miss.
public sealed class UserAccessCache : IUserAccessCache
{
    private readonly ConcurrentDictionary<int, bool> _deactivated = new();

    public bool? IsDeactivated(int userId) => _deactivated.TryGetValue(userId, out var value) ? value : null;

    public void Remember(int userId, bool deactivated) => _deactivated[userId] = deactivated;
}
