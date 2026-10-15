using HotelPOS.Application.Features.Licensing;
using HotelPOS.Contracts.Common;

namespace HotelPOS.Api.Common;

// An expired licence (or ended trial) makes the server read-only: reads and exports keep working, every write is
// refused with LICENSE_EXPIRED, except signing in/out, applying a key, and taking a backup. Only administrators can
// sign in then (LoginCommand), so they can look at their data and apply a new key.
public sealed class LicenseEnforcementMiddleware
{
    private static readonly string[] AlwaysAllowed = { "/api/auth", "/api/license", "/api/admin/backup" };

    private readonly RequestDelegate _next;

    public LicenseEnforcementMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ILicenseService licenses)
    {
        var method = context.Request.Method;
        var isWrite = !HttpMethods.IsGet(method) && !HttpMethods.IsHead(method) && !HttpMethods.IsOptions(method);
        if (isWrite
            && context.Request.Path.StartsWithSegments("/api")
            && !AlwaysAllowed.Any(p => context.Request.Path.StartsWithSegments(p))
            && context.User.Identity?.IsAuthenticated == true)
        {
            var evaluation = await licenses.EvaluateAsync(context.RequestAborted);
            if (evaluation.IsExpired)
            {
                await ApiResponseWriter.WriteErrorAsync(
                    context,
                    StatusCodes.Status403Forbidden,
                    ErrorCodes.LicenseExpired,
                    (evaluation.Problem ?? "The licence has expired.") + " HotelPOS is read-only until an administrator applies a licence key.");
                return;
            }
        }

        await _next(context);
    }
}
