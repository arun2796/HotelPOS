using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Security;
using HotelPOS.Application.Security;
using HotelPOS.Contracts.Common;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Api.Common;

public sealed class DeviceEnforcementMiddleware
{
    private readonly RequestDelegate _next;

    public DeviceEnforcementMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IAppDbContext db, IDeviceAccessCache cache)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var deviceIdClaim = context.User.FindFirst(PosClaimTypes.DeviceId)?.Value;
            if (Guid.TryParse(deviceIdClaim, out var deviceId))
            {
                var isDeactivated = cache.IsDeactivated(deviceId);
                if (isDeactivated is null)
                {
                    isDeactivated = await db.Devices.AsNoTracking()
                        .Where(d => d.Id == deviceId && !d.IsActive)
                        .AnyAsync(context.RequestAborted);
                    cache.Remember(deviceId, isDeactivated.Value);
                }

                if (isDeactivated.Value)
                {
                    await ApiResponseWriter.WriteErrorAsync(
                        context,
                        StatusCodes.Status403Forbidden,
                        ErrorCodes.DeviceDisabled,
                        "Device is disabled. Contact your administrator.");
                    return;
                }
            }
        }

        await _next(context);
    }
}

