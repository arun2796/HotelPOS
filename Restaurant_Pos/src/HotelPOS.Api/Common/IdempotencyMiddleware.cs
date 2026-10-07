using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Contracts.Common;

namespace HotelPOS.Api.Common;

[AttributeUsage(AttributeTargets.Method)]
public sealed class IdempotentAttribute : Attribute
{
}

public sealed class IdempotencyMiddleware
{
    public const string ReplayedHeader = "Idempotent-Replayed";

    private readonly RequestDelegate _next;
    private readonly ILogger<IdempotencyMiddleware> _logger;

    public IdempotencyMiddleware(RequestDelegate next, ILogger<IdempotencyMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IIdempotencyStore store, ICurrentUser currentUser)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<IdempotentAttribute>() is null)
        {
            await _next(context);
            return;
        }

        if (!Guid.TryParse(context.Request.Headers[PosHeaders.IdempotencyKey], out var key) || key == Guid.Empty)
        {
            await ApiResponseWriter.WriteErrorAsync(context, StatusCodes.Status400BadRequest, ErrorCodes.ValidationError,
                $"The {PosHeaders.IdempotencyKey} header (a GUID) is required for this request.");
            return;
        }

        var route = $"{context.Request.Method} {context.Request.Path}";
        var hash = await HashRequestAsync(context.Request, route);
        var begin = await store.BeginAsync(key, currentUser.UserId, route, hash, context.RequestAborted);
        switch (begin.Outcome)
        {
            case IdempotencyOutcome.Replay:
                _logger.LogInformation("Replaying stored response for idempotency key {Key}", key);
                context.Response.StatusCode = begin.StatusCode;
                context.Response.ContentType = "application/json; charset=utf-8";
                context.Response.Headers[ReplayedHeader] = "true";
                await context.Response.WriteAsync(begin.ResponseBody ?? string.Empty, context.RequestAborted);
                return;
            case IdempotencyOutcome.KeyReused:
                await ApiResponseWriter.WriteErrorAsync(context, StatusCodes.Status409Conflict, ErrorCodes.IdempotencyKeyReused,
                    "This request key was already used for a different request.");
                return;
            case IdempotencyOutcome.InProgress:
                await ApiResponseWriter.WriteErrorAsync(context, StatusCodes.Status409Conflict, ErrorCodes.RequestInProgress,
                    "The same request is still being processed. Wait a moment and try again.");
                return;
        }

        var original = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await _next(context);
        }
        catch
        {
            context.Response.Body = original;
            await store.AbandonAsync(key, CancellationToken.None);
            throw;
        }

        context.Response.Body = original;
        var body = buffer.ToArray();

        // Server errors are not stored, so the client's retry with the same key is processed again.
        if (context.Response.StatusCode < StatusCodes.Status500InternalServerError)
        {
            await store.CompleteAsync(key, context.Response.StatusCode, Encoding.UTF8.GetString(body), CancellationToken.None);
        }
        else
        {
            await store.AbandonAsync(key, CancellationToken.None);
        }

        await original.WriteAsync(body, context.RequestAborted);
    }

    private static async Task<string> HashRequestAsync(HttpRequest request, string route)
    {
        request.EnableBuffering();
        using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        request.Body.Position = 0;

        var canonical = route + "\n" + Canonicalize(body);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    // Property order and whitespace must not make two identical requests look different.
    private static string Canonicalize(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        try
        {
            return Sort(JsonNode.Parse(body))?.ToJsonString() ?? "null";
        }
        catch (JsonException)
        {
            return body;
        }
    }

    private static JsonNode? Sort(JsonNode? node) => node switch
    {
        JsonObject obj => new JsonObject(obj
            .OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .Select(p => KeyValuePair.Create(p.Key.ToLowerInvariant(), Sort(p.Value?.DeepClone())))),
        JsonArray array => new JsonArray(array.Select(item => Sort(item?.DeepClone())).ToArray()),
        _ => node?.DeepClone(),
    };
}
