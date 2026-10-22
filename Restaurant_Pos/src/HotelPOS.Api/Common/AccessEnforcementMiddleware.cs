using System.Globalization;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Security;
using HotelPOS.Application.Security;
using HotelPOS.Contracts.Common;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Api.Common;

// A token stays valid until it expires (up to an hour), so a deactivated device or user is refused on every request
// here, not only at the next sign-in or token renewal. The same check enforces "must change password": a password
// set by an administrator (first admin, reset) opens nothing but the auth endpoints until the user replaces it.
// Both answers are cached and dropped by the admin actions and the password commands.
public sealed class AccessEnforcementMiddleware
{
    private readonly RequestDelegate _next;

    public AccessEnforcementMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IAppDbContext db, IDeviceAccessCache devices, IUserAccessCache users)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            if (Guid.TryParse(context.User.FindFirst(PosClaimTypes.DeviceId)?.Value, out var deviceId)
                && await IsDeviceDeactivatedAsync(db, devices, deviceId, context.RequestAborted))
            {
                await ApiResponseWriter.WriteErrorAsync(
                    context, StatusCodes.Status403Forbidden, ErrorCodes.DeviceDisabled, "Device is disabled. Contact your administrator.");
                return;
            }

            if (int.TryParse(context.User.FindFirst(PosClaimTypes.Subject)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var userId))
            {
                var state = await UserStateAsync(db, users, userId, context.RequestAborted);
                if (state.Deactivated)
                {
                    await ApiResponseWriter.WriteErrorAsync(
                        context, StatusCodes.Status403Forbidden, ErrorCodes.AccountDisabled, "This account is disabled. Contact your manager.");
                    return;
                }

                if (state.MustChangePassword && RequiresCurrentPassword(context.Request.Path))
                {
                    await ApiResponseWriter.WriteErrorAsync(
                        context, StatusCodes.Status403Forbidden, ErrorCodes.PasswordChangeRequired,
                        "Change your password before continuing. The one you signed in with was set by an administrator.");
                    return;
                }
            }
        }

        await _next(context);
    }

    // Only the API proper: signing in, renewing, signing out, reading one's own profile and changing the password
    // stay open (/api/auth/**), as do the version/update check, the hub and the static files.
    public static bool RequiresCurrentPassword(PathString path) =>
        path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
        && !path.StartsWithSegments("/api/auth", StringComparison.OrdinalIgnoreCase)
        && !path.StartsWithSegments("/api/system/info", StringComparison.OrdinalIgnoreCase);

    private static async Task<bool> IsDeviceDeactivatedAsync(IAppDbContext db, IDeviceAccessCache cache, Guid deviceId, CancellationToken cancellationToken)
    {
        if (cache.IsDeactivated(deviceId) is { } known)
        {
            return known;
        }

        var deactivated = await db.Devices.AsNoTracking().AnyAsync(d => d.Id == deviceId && !d.IsActive, cancellationToken);
        cache.Remember(deviceId, deactivated);
        return deactivated;
    }

    private static async Task<UserAccessState> UserStateAsync(IAppDbContext db, IUserAccessCache cache, int userId, CancellationToken cancellationToken)
    {
        if (cache.Get(userId) is { } known)
        {
            return known;
        }

        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.IsActive, u.MustChangePassword })
            .FirstOrDefaultAsync(cancellationToken);
        // A token for a user who no longer exists is treated as deactivated.
        var state = new UserAccessState(Deactivated: user is null || !user.IsActive, MustChangePassword: user?.MustChangePassword ?? false);
        cache.Remember(userId, state);
        return state;
    }
}
