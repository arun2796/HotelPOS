using HotelPOS.Application.Common.Interfaces;
using Serilog;
using Serilog.Context;

namespace HotelPOS.Api.Common;

public sealed class RequestContextLoggingMiddleware
{
    public const string ActorProperty = "Actor";

    private readonly RequestDelegate _next;

    public RequestContextLoggingMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ICurrentUser currentUser, IDiagnosticContext diagnosticContext)
    {
        var actor = Describe(currentUser);
        if (actor is null)
        {
            await _next(context);
            return;
        }

        // Also attach it to the request-completed event written by UseSerilogRequestLogging.
        diagnosticContext.Set(ActorProperty, actor);
        using (LogContext.PushProperty(ActorProperty, actor))
        {
            await _next(context);
        }
    }

    private static string? Describe(ICurrentUser user) =>
        user.UserName is null ? null
        : user.DeviceName is null ? user.UserName
        : $"{user.UserName}@{user.DeviceName}";
}
