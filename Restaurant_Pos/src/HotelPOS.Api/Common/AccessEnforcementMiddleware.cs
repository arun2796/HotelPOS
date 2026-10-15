using System.Globalization;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Security;
using HotelPOS.Application.Security;
using HotelPOS.Contracts.Common;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Api.Common;

// A token stays valid until it expires (up to an hour), so a deactivated device or user is refused on every request
// here, not only at the next sign-in or token renewal. Both answers are cached and updated by the admin actions.
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

            if (int.TryParse(context.User.FindFirst(PosClaimTypes.Subject)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var userId)
                && await IsUserDeactivatedAsync(db, users, userId, context.RequestAborted))
            {
                await ApiResponseWriter.WriteErrorAsync(
                    context, StatusCodes.Status403Forbidden, ErrorCodes.AccountDisabled, "This account is disabled. Contact your manager.");
                return;
            }
        }

        await _next(context);
    }

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

    private static async Task<bool> IsUserDeactivatedAsync(IAppDbContext db, IUserAccessCache cache, int userId, CancellationToken cancellationToken)
    {
        if (cache.IsDeactivated(userId) is { } known)
        {
            return known;
        }

        var deactivated = await db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && !u.IsActive, cancellationToken);
        cache.Remember(userId, deactivated);
        return deactivated;
    }
}
