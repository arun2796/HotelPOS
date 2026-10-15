using System.Text.RegularExpressions;
using HotelPOS.Contracts.Common;
using Serilog.Context;

namespace HotelPOS.Api.Common;

public sealed partial class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers[PosHeaders.CorrelationId].ToString();
        var correlationId = IsAcceptable(incoming) ? incoming : Guid.NewGuid().ToString("N");

        context.TraceIdentifier = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[PosHeaders.CorrelationId] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }

    private static bool IsAcceptable(string value) =>
        !string.IsNullOrEmpty(value) && value.Length <= 64 && SafeId().IsMatch(value);

    [GeneratedRegex("^[A-Za-z0-9_.:-]+$")]
    private static partial Regex SafeId();
}
