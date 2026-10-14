using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Contracts.Admin;
using HotelPOS.Domain.Identity;

namespace HotelPOS.Application.Features.Security;

// Every place that checks a user's password (login, manager approval) counts a wrong one towards the same lockout,
// so no endpoint can be used to guess a password without limit.
internal static class LoginFailures
{
    public static async Task<bool> RecordAsync(
        IAppDbContext db,
        IAuditTrail audit,
        User user,
        DateTime nowUtc,
        string reason,
        string? deviceName,
        CancellationToken cancellationToken)
    {
        var threshold = await db.IntSettingAsync(SettingKeys.LockoutThreshold, 5, cancellationToken);
        var minutes = await db.IntSettingAsync(SettingKeys.LockoutMinutes, 15, cancellationToken);
        var locked = user.RecordFailedLogin(nowUtc, threshold, TimeSpan.FromMinutes(minutes));

        var actor = new AuditActor(user.Id, user.Username);
        if (locked)
        {
            audit.Add(AuditActions.UserLockedOut, nameof(User), user.Id.ToString(),
                newValues: new { user.FailedLoginCount, user.LockedUntil, Reason = reason, DeviceName = deviceName }, actor: actor);
        }
        else
        {
            audit.Add(AuditActions.LoginFailed, nameof(User), user.Id.ToString(),
                newValues: new { Reason = reason, user.FailedLoginCount, DeviceName = deviceName }, actor: actor);
        }

        return locked;
    }
}
