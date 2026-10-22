using System.Collections.Concurrent;

namespace HotelPOS.Application.Security;

// What AccessEnforcementMiddleware needs to know about a signed-in user on every request.
public readonly record struct UserAccessState(bool Deactivated, bool MustChangePassword);

public interface IUserAccessCache
{
    UserAccessState? Get(int userId);

    void Remember(int userId, UserAccessState state);

    // Drop the entry so the next request reads the database again (after a password change or reset, a (de)activation).
    void Forget(int userId);
}

// The user counterpart of DeviceAccessCache: an access token stays valid for up to an hour, so without a
// per-request check a deactivated user could keep working until it expires, and a user who must change the
// password could keep using the old one. Filled from the database on a miss; the commands that change the state
// forget the entry after their commit.
public sealed class UserAccessCache : IUserAccessCache
{
    private readonly ConcurrentDictionary<int, UserAccessState> _states = new();

    public UserAccessState? Get(int userId) => _states.TryGetValue(userId, out var value) ? value : null;

    public void Remember(int userId, UserAccessState state) => _states[userId] = state;

    public void Forget(int userId) => _states.TryRemove(userId, out _);
}
